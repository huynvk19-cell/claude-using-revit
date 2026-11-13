# -*- coding: utf-8 -*-
"""Standards database: the sheet / view catalogue per building type + the log of the user's corrections, in SQLite.
Plain Python 3, no packages. The text files are the source; the database is rebuilt from them on every call:
    domain/drafting-sheet-layout.md §4, domain/drafting-sheet-set-<type>.md   the drawing tables (catalogue)
    domain/drafting-coverage.md §2                                           kind -> skill, commands, level
    data/condition-keys.csv, data/condition-map.csv                          condition text -> keys
    data/corrections.csv                                                     the corrections log

    python standards_db.py types
    python standards_db.py sheets --type villa --stage KT --has elevator,pool
    python standards_db.py corrections --scope villa --status open
    python standards_db.py correction-add --date 2026-11-13 --slug no-zone-lines --scope all --topic layout --rule "..."
    python standards_db.py correction-set 2026-11-13-no-zone-lines --status in-repo --ref "domain/x.md LO13"
    python standards_db.py sql "SELECT type, count(*) FROM catalog_row GROUP BY type"
    python standards_db.py check          every text file is consistent (CI); exit 1 on a problem
    python standards_db.py build          write standards.db next to this file (to open in a SQLite viewer)
Details: domain/drafting-standards-db.md."""
import argparse
import csv
import glob
import io
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UP = os.path.dirname(os.path.dirname(HERE))
IN_REPO = os.path.isdir(os.path.join(UP, 'domain'))          # repo checkout; else the installed copy (drafting-domain/)
DOMAIN = os.path.join(UP, 'domain') if IN_REPO else UP
DATA = os.path.join(HERE, 'data')
CORRECTIONS = os.path.join(DATA, 'corrections.csv')
CORRECTION_FIELDS = ['id', 'date', 'scope', 'kind', 'topic', 'rule', 'why', 'status', 'repo_ref', 'superseded_by']
STATUSES = ('open', 'in-repo', 'superseded')
STAGES = ('YT', 'CS', 'KT', 'TC', 'HC')
TICK = re.compile(r'`([^`]+)`')
SCALE = re.compile(r'1:(\d+)')
DASH = (u'—', u'-', u'')


def read(path):
    with io.open(path, encoding='utf-8') as f:
        return f.read()


def read_csv(path):
    with io.open(path, encoding='utf-8', newline='') as f:
        return list(csv.DictReader(f))


def write_corrections(rows):
    rows = sorted(rows, key=lambda r: r['id'])
    with io.open(CORRECTIONS, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, CORRECTION_FIELDS, lineterminator='\n')
        w.writeheader()
        w.writerows(rows)


def tables(text):
    """Every markdown table as (heading above it, header cells, [(line number, row cells)])."""
    out, head, rows, heading, at = [], None, [], '', ''
    for n, line in enumerate(text.split('\n'), 1):
        if line.startswith('#'):
            heading = line.lstrip('#').strip()
        if not line.startswith('|'):
            if head:
                out.append((at, head, rows))
            head, rows = None, []
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if head is None:
            head, at = cells, heading
        elif not set(line) <= set('|-: '):
            rows.append((n, cells))
    if head:
        out.append((at, head, rows))
    return out


def keys_of(expr):
    return [k for k in re.split(r'[&|]', expr) if k]


def holds(expr, have):
    """a&b|c: true when every key of one of the | parts is in `have`. '' = always."""
    return not expr or any(all(k in have for k in part.split('&')) for part in expr.split('|'))


def split_list(text):
    return [x.strip() for x in text.split(',') if x.strip()]


def load():
    """Read every text source. Returns (data dict, problems)."""
    problems = []
    d = {'types': [], 'kinds': [], 'conditions': [], 'rows': [], 'corrections': []}

    # ---- kinds: the coverage matrix
    for _, head, rows in tables(read(os.path.join(DOMAIN, 'drafting-coverage.md'))):
        if head[:2] != [u'Loại', u'Bản vẽ']:
            continue
        for n, c in rows:
            r = dict(zip(head, c))
            d['kinds'].append({
                'key': TICK.findall(r[u'Loại'])[0], 'name': r[u'Bản vẽ'],
                'skills': ', '.join(TICK.findall(r['Skill'])), 'workflow': ', '.join(TICK.findall(r['Workflow'])),
                'do_commands': ', '.join(TICK.findall(r[u'Lệnh làm'])),
                'audit_commands': ', '.join(TICK.findall(r[u'Lệnh kiểm'])),
                'level': int(r[u'Mức']), 'missing': '' if r[u'Còn thiếu'] in DASH else r[u'Còn thiếu']})
    kinds = {k['key'] for k in d['kinds']}
    if not kinds:
        problems.append('drafting-coverage.md: no kind table read')

    # ---- conditions
    d['conditions'] = read_csv(os.path.join(DATA, 'condition-keys.csv'))
    cond_keys = {c['key'] for c in d['conditions']}
    cond_map = {}
    for r in read_csv(os.path.join(DATA, 'condition-map.csv')):
        cond_map[r['text']] = r['expr']
        for k in keys_of(r['expr']):
            if k not in cond_keys:
                problems.append('condition-map.csv: "%s" uses key %s, not in condition-keys.csv' % (r['text'], k))
    used_texts = set()

    # ---- building types and their drawing rows
    files = [os.path.join(DOMAIN, 'drafting-sheet-layout.md')] + sorted(glob.glob(os.path.join(DOMAIN, 'drafting-sheet-set-*.md')))
    for f in files:
        name = os.path.basename(f)
        key = 'core' if name == 'drafting-sheet-layout.md' else name[len('drafting-sheet-set-'):-3]
        text = read(f)
        title = re.search(r'^# (.+)$', text, re.M)
        paper = []
        m = re.search(u'\\*\\*Khổ giấy\\*\\*:([^.\n]*)', text) if key != 'core' else None
        if m:
            for size in re.findall(r'\bA[0-4]\b', m.group(1)):
                if size not in paper:
                    paper.append(size)
        replaces, seq = 0, 0
        for heading, head, rows in tables(text):
            if u'Bản vẽ' not in head or u'Tỷ lệ' not in head or u'GĐ' not in head:
                continue
            if u'Danh mục điển hình' in heading:
                replaces = 1
            for n, c in rows:
                where = '%s:%d' % (name, n)
                if len(c) != len(head):
                    problems.append('%s: %d cells, the header has %d' % (where, len(c), len(head)))
                    continue
                r = dict(zip(head, c))
                seq += 1
                scales = [int(x) for x in SCALE.findall(r[u'Tỷ lệ'])]
                cond = r.get(u'Điều kiện', '')
                cond = '' if cond in DASH else cond
                expr = ''
                if cond:
                    used_texts.add(cond)
                    expr = cond_map.get(cond)
                    if expr is None:
                        problems.append('%s: condition "%s" has no row in condition-map.csv' % (where, cond))
                        expr = ''
                if r[u'GĐ'] not in STAGES:
                    problems.append('%s: GĐ %s is not one of %s' % (where, r[u'GĐ'], ' / '.join(STAGES)))
                    continue
                grp = r.get(u'Nhóm', '')
                if not re.match(r'^[0-8]$', grp):
                    problems.append('%s: Nhóm "%s" is not a group 0-8' % (where, grp))
                    grp = None
                row_kinds = TICK.findall(r.get(u'Loại', ''))
                for k in row_kinds:
                    if k not in kinds:
                        problems.append('%s: Loại %s is not in drafting-coverage.md' % (where, k))
                content = r.get(u'Nội dung bắt buộc', '')
                d['rows'].append({
                    'type': key, 'seq': seq, 'section': heading, 'grp': None if grp is None else int(grp),
                    'drawing': r[u'Bản vẽ'], 'scale': '' if r[u'Tỷ lệ'] in DASH else r[u'Tỷ lệ'],
                    'scale_coarse': max(scales) if scales else None, 'scale_fine': min(scales) if scales else None,
                    'stage': r[u'GĐ'], 'condition': cond, 'condition_expr': expr,
                    'content': '' if content in DASH else content, 'source': where,
                    'kinds': [k for k in row_kinds if k in kinds]})
        if key != 'core' and not seq:
            problems.append(name + ': no drawing table read')
        d['types'].append({'key': key, 'name': u'Bộ hồ sơ lõi' if key == 'core' else re.sub(u'^Bộ hồ sơ: ', '', title.group(1).strip()) if title else key, 'paper': ', '.join(paper),
                           'replaces_core': replaces, 'source_file': name})
    for t in sorted(set(cond_map) - used_texts):
        problems.append('condition-map.csv: "%s" is used by no drawing row' % t)
    used_keys = {k for e in cond_map.values() for k in keys_of(e)}
    for k in sorted(cond_keys - used_keys):
        problems.append('condition-keys.csv: key %s is used by no condition' % k)

    # ---- corrections
    types = {t['key'] for t in d['types']} - {'core'}
    rows = read_csv(CORRECTIONS)
    ids = [r['id'] for r in rows]
    if ids != sorted(ids):
        problems.append('corrections.csv: rows are not sorted by id')
    for r in rows:
        where = 'corrections.csv %s' % r['id']
        if None in r.values() or set(r) != set(CORRECTION_FIELDS):
            problems.append(where + ': wrong number of columns')
            continue
        if ids.count(r['id']) > 1:
            problems.append(where + ': id used twice')
        if not re.match(r'^\d{4}-\d{2}-\d{2}$', r['date']) or not re.match(r'^%s-[a-z0-9]+(-[a-z0-9]+)*$' % re.escape(r['date']), r['id']):
            problems.append(where + ': id must be <date>-<slug> (a-z, 0-9, -) and date YYYY-MM-DD')
        scope = split_list(r['scope'])
        if not scope or (scope != ['all'] and [s for s in scope if s not in types]):
            problems.append('%s: scope "%s" must be all or building types (%s)' % (where, r['scope'], ', '.join(sorted(types))))
        for k in split_list(r['kind']):
            if k not in kinds:
                problems.append('%s: kind %s is not in drafting-coverage.md' % (where, k))
        if not r['topic'] or not r['rule']:
            problems.append(where + ': topic and rule are required')
        if r['status'] not in STATUSES:
            problems.append('%s: status %s must be one of %s' % (where, r['status'], ' / '.join(STATUSES)))
        if r['status'] == 'in-repo' and not r['repo_ref']:
            problems.append(where + ': in-repo needs repo_ref (where the rule lives now)')
        if r['status'] == 'superseded' and r['superseded_by'] not in ids:
            problems.append(where + ': superseded needs superseded_by = an existing id')
        for ref in [x.strip() for x in r['repo_ref'].split(';') if x.strip()]:
            path = ref.split(' ')[0]
            found = os.path.exists(os.path.join(UP, path)) if IN_REPO else True
            if not found:
                problems.append('%s: repo_ref file %s does not exist' % (where, path))
    d['corrections'] = rows
    return d, problems


def connect(d, path=':memory:'):
    con = sqlite3.connect(path)
    con.row_factory = sqlite3.Row
    con.executescript(read(os.path.join(HERE, 'schema.sql')))
    con.executemany('INSERT INTO building_type VALUES (:key, :name, :paper, :replaces_core, :source_file)', d['types'])
    con.executemany('INSERT OR IGNORE INTO kind VALUES (:key, :name, :skills, :workflow, :do_commands, :audit_commands, :level, :missing)', d['kinds'])
    con.executemany('INSERT OR IGNORE INTO condition VALUES (:key, :label)', d['conditions'])
    for i, r in enumerate(d['rows'], 1):
        con.execute('INSERT INTO catalog_row VALUES (?, :type, :seq, :section, :grp, :drawing, :scale, :scale_coarse, '
                    ':scale_fine, :stage, :condition, :condition_expr, :content, :source)'.replace('?', str(i)), r)
        con.executemany('INSERT OR IGNORE INTO catalog_row_kind VALUES (?, ?)', [(i, k) for k in r['kinds']])
        con.executemany('INSERT OR IGNORE INTO catalog_row_condition VALUES (?, ?)', [(i, k) for k in keys_of(r['condition_expr'])])
    con.executemany('INSERT OR IGNORE INTO correction VALUES (:id, :date, :scope, :kind, :topic, :rule, :why, :status, '
                    ':repo_ref, :superseded_by)', [r for r in d['corrections'] if set(r) == set(CORRECTION_FIELDS) and None not in r.values()])
    con.commit()
    return con


def show(rows, columns, as_json):
    rows = [dict(r) for r in rows]
    if as_json:
        print(json.dumps(rows, ensure_ascii=False, indent=1))
        return
    print(' | '.join(columns))
    for r in rows:
        print(' | '.join('' if r[c] is None else str(r[c]) for c in columns))


# ---- commands
def cmd_types(con, a):
    rows = con.execute(
        "SELECT t.key, t.name, t.paper, CASE WHEN t.key = 'core' THEN 'core' WHEN t.replaces_core = 1 THEN 'own list' "
        "ELSE 'core +' END AS list, (SELECT count(*) FROM v_set s WHERE s.set_type = t.key) AS rows "
        "FROM building_type t ORDER BY t.key").fetchall()
    show(rows, ['key', 'name', 'paper', 'list', 'rows'], a.json)


def cmd_sheets(con, a):
    if not con.execute('SELECT 1 FROM building_type WHERE key = ?', (a.type,)).fetchone():
        sys.exit('unknown type %s: run "types"' % a.type)
    sql, args = 'SELECT * FROM v_set WHERE set_type = ?', [a.type]
    if a.stage:
        sql += ' AND stage_rank <= (SELECT rank FROM stage WHERE key = ?)'
        args.append(a.stage)
    if a.group is not None:
        sql += ' AND grp = ?'
        args.append(a.group)
    if a.kind:
        sql += ' AND id IN (SELECT row_id FROM catalog_row_kind WHERE kind = ?)'
        args.append(a.kind)
    if a.section:
        sql += ' AND section LIKE ?'
        args.append('%' + a.section + '%')
    if a.max_level:
        sql += ' AND level <= ?'
        args.append(a.max_level)
    rows = con.execute(sql + " ORDER BY type <> 'core', seq", args).fetchall()
    dropped = []
    if a.has is not None:
        have = set(split_list(a.has)) - {'none'}
        unknown = have - {r['key'] for r in con.execute('SELECT key FROM condition')}
        if unknown:
            sys.exit('unknown condition %s: run "conditions"' % ', '.join(sorted(unknown)))
        dropped = [r for r in rows if not holds(r['condition_expr'], have)]
        rows = [r for r in rows if holds(r['condition_expr'], have)]
    columns = ['source', 'grp', 'drawing', 'scale', 'stage', 'condition', 'kinds', 'level']
    show(rows, columns + (['content'] if a.detail else []), a.json)
    if not a.json:
        t = con.execute('SELECT paper FROM building_type WHERE key = ?', (a.type,)).fetchone()
        by = {}
        for r in rows:
            by[r['level']] = by.get(r['level'], 0) + 1
        print('%d rows; paper %s; by level: %s' % (len(rows), t['paper'] or 'see the profile',
                                                  ', '.join('%s: %d' % (k, by[k]) for k in sorted(by, key=lambda x: -(x or 0)))))
        if dropped:
            print('n/a (condition not met): ' + '; '.join('%s [%s]' % (r['drawing'], r['condition']) for r in dropped))
        elif a.has is None and any(r['condition'] for r in rows):
            print('conditional rows are all listed: pass --has <keys> (or --has none) to keep only the ones that apply')


def cmd_conditions(con, a):
    show(con.execute('SELECT c.key, c.label, (SELECT count(*) FROM catalog_row_condition x WHERE x.condition = c.key) AS rows '
                     'FROM condition c ORDER BY c.key').fetchall(), ['key', 'label', 'rows'], a.json)


def cmd_corrections(con, a):
    sql, args = 'SELECT * FROM correction WHERE 1', []
    if a.scope:
        sql += " AND (scope = 'all' OR ', ' || scope || ', ' LIKE ?)"
        args.append('%, ' + a.scope + ', %')
    if a.kind:
        sql += " AND (', ' || kind || ', ' LIKE ?)"
        args.append('%, ' + a.kind + ', %')
    if a.topic:
        sql += ' AND topic LIKE ?'
        args.append('%' + a.topic + '%')
    if a.status:
        sql += ' AND status = ?'
        args.append(a.status)
    elif not a.all:
        sql += " AND status <> 'superseded'"
    if a.text:
        sql += ' AND (rule LIKE ? OR why LIKE ?)'
        args += ['%' + a.text + '%'] * 2
    if a.since:
        sql += ' AND date >= ?'
        args.append(a.since)
    rows = con.execute(sql + ' ORDER BY id', args).fetchall()
    show(rows, ['id', 'scope', 'kind', 'topic', 'rule', 'status', 'repo_ref'] + (['why', 'superseded_by'] if a.detail else []), a.json)
    if not a.json:
        n = con.execute("SELECT count(*) FROM correction WHERE status = 'open'").fetchone()[0]
        print('%d rows; %d open in the whole log (said by the user, not yet in a standard)' % (len(rows), n))


def need_repo():
    if not IN_REPO:
        sys.exit('this is the installed copy: the next install overwrites it. Add the correction in the repo checkout '
                 '(tools/standards-db/), or put it in the report so it reaches the repo.')


def cmd_correction_add(a):
    need_repo()
    rows = read_csv(CORRECTIONS)
    new = {'id': '%s-%s' % (a.date, a.slug), 'date': a.date, 'scope': a.scope, 'kind': a.kind or '', 'topic': a.topic,
           'rule': a.rule, 'why': a.why or '', 'status': 'in-repo' if a.ref else 'open', 'repo_ref': a.ref or '',
           'superseded_by': ''}
    if new['id'] in [r['id'] for r in rows]:
        sys.exit('id %s exists: pick another --slug, or use correction-set' % new['id'])
    finish(rows + [new], rows, 'added ' + new['id'] + ' (' + new['status'] + ')')


def cmd_correction_set(a):
    need_repo()
    rows = read_csv(CORRECTIONS)
    before = [dict(r) for r in rows]
    hit = [r for r in rows if r['id'] == a.id]
    if not hit:
        sys.exit('no correction %s' % a.id)
    r = hit[0]
    for field, value in [('status', a.status), ('repo_ref', a.ref), ('superseded_by', a.superseded_by), ('rule', a.rule),
                         ('why', a.why), ('scope', a.scope), ('kind', a.kind), ('topic', a.topic)]:
        if value is not None:
            r[field] = value
    if a.ref and a.status is None and r['status'] == 'open':
        r['status'] = 'in-repo'
    if a.superseded_by and a.status is None:
        r['status'] = 'superseded'
    finish(rows, before, 'updated %s (%s)' % (r['id'], r['status']))


def finish(rows, before, message):
    """Write the log; put the old one back when the new one does not pass the check."""
    write_corrections(rows)
    problems = [p for p in load()[1] if p.startswith('corrections.csv')]
    if problems:
        write_corrections(before)
        sys.exit('not saved:\n' + '\n'.join(problems))
    print(message)


def cmd_sql(con, a):
    if not re.match(r'^\s*(select|with)\b', a.query, re.I):
        sys.exit('read only: SELECT / WITH. The data is changed in the text files.')
    try:
        cur = con.execute(a.query)
    except sqlite3.Error as e:
        sys.exit('sql: %s' % e)
    show(cur.fetchall(), [c[0] for c in cur.description], a.json)


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8')
    p = argparse.ArgumentParser(description='Sheet / view catalogue per building type + the corrections log (SQLite).')
    sub = p.add_subparsers(dest='cmd')
    sub.required = True

    def add(name, help_text):
        s = sub.add_parser(name, help=help_text)
        s.add_argument('--json', action='store_true', help='rows as JSON')
        return s

    add('types', 'building types: paper, own list or core +, row count')
    add('conditions', 'condition keys for --has')
    s = add('sheets', 'the drawings a set needs')
    s.add_argument('--type', required=True, help='building type key (types)')
    s.add_argument('--stage', choices=STAGES, help='keep rows needed at this stage or earlier')
    s.add_argument('--has', help='facts that hold, comma separated (conditions); "none" = no conditional row')
    s.add_argument('--kind', help='one drawing kind (Loại)')
    s.add_argument('--group', type=int, help='sheet group 0-8')
    s.add_argument('--section', help='text of the heading above the table (sub-type)')
    s.add_argument('--max-level', type=int, help='rows at this coverage level or lower (1 = by hand)')
    s.add_argument('--detail', action='store_true', help='add the mandatory content')
    s = add('corrections', 'the log of what the user corrected')
    s.add_argument('--scope', help='building type: its rows + the rows for all')
    s.add_argument('--kind', help='drawing kind')
    s.add_argument('--topic', help='text in the topic')
    s.add_argument('--status', choices=STATUSES)
    s.add_argument('--text', help='text in the rule or the reason')
    s.add_argument('--since', help='YYYY-MM-DD')
    s.add_argument('--all', action='store_true', help='superseded rows too')
    s.add_argument('--detail', action='store_true', help='add the reason')
    s = sub.add_parser('correction-add', help='log one correction (repo checkout only)')
    s.add_argument('--date', required=True, help='the day the user said it, YYYY-MM-DD')
    s.add_argument('--slug', required=True, help='short name, a-z 0-9 -: the id is <date>-<slug>')
    s.add_argument('--scope', required=True, help='all, or building type keys')
    s.add_argument('--topic', required=True)
    s.add_argument('--rule', required=True, help='the correction as a rule')
    s.add_argument('--kind', help='drawing kind keys')
    s.add_argument('--why', help="the user's reason")
    s.add_argument('--ref', help='file + § / rule code where it lives now; given = status in-repo, else open')
    s = sub.add_parser('correction-set', help='change one correction: status, ref...')
    s.add_argument('id')
    for flag in ('--status', '--ref', '--superseded-by', '--rule', '--why', '--scope', '--kind', '--topic'):
        s.add_argument(flag, choices=STATUSES if flag == '--status' else None)
    s = add('sql', 'any SELECT on the database (tables: schema.sql)')
    s.add_argument('query')
    sub.add_parser('check', help='the text files are consistent; exit 1 on a problem')
    sub.add_parser('build', help='write standards.db next to this file')
    a = p.parse_args()

    if a.cmd == 'correction-add':
        return cmd_correction_add(a)
    if a.cmd == 'correction-set':
        return cmd_correction_set(a)
    d, problems = load()
    if a.cmd == 'check':
        for x in problems:
            print('FAIL ' + x)
        con = connect(d)
        print('standards db: %d building types, %d drawing rows, %d kinds, %d conditions, %d corrections (%d open); %d problem(s)'
              % (len(d['types']), len(d['rows']), len(d['kinds']), len(d['conditions']), len(d['corrections']),
                 con.execute("SELECT count(*) FROM correction WHERE status = 'open'").fetchone()[0], len(problems)))
        sys.exit(1 if problems else 0)
    if problems:
        sys.stderr.write('warning: %d problem(s) in the text files, run "check"\n' % len(problems))
    if a.cmd == 'build':
        path = os.path.join(HERE, 'standards.db')
        if os.path.exists(path):
            os.remove(path)
        connect(d, path).close()
        print('standards.db: %d drawing rows, %d corrections' % (len(d['rows']), len(d['corrections'])))
        return
    {'types': cmd_types, 'conditions': cmd_conditions, 'sheets': cmd_sheets, 'corrections': cmd_corrections,
     'sql': cmd_sql}[a.cmd](connect(d), a)


if __name__ == '__main__':
    main()
