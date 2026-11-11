# -*- coding: utf-8 -*-
"""Coverage matrix (domain/drafting-coverage.md) against this repo's own files, on plain Python 3:
    python addin/tests/coverage_check.py
- every skill, workflow and command the matrix names exists; a command marked "(chưa có)" really is missing;
- a level has what it claims: 2 = a skill, 3 = a do command, 4 = an audit command and a workflow too;
- every drawing row of drafting-sheet-layout.md §4 and of the sheet-set files has a `Loại` the matrix knows.
Prints the matrix per building type (rows per level). Exit 1 on a problem."""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOMAIN = os.path.join(ROOT, 'domain')
BUILT_IN = {'cur_task', 'cur_status', 'run_dynamic_command'}
TICK = re.compile(r'`([^`]+)`')
problems = []


def read(path):
    with open(path, encoding='utf-8') as f:
        return f.read()


def tables(text):
    """Every markdown table as (header cells, [(line number, row cells)])."""
    out, head, rows = [], None, []
    for n, line in enumerate(text.split('\n'), 1):
        if not line.startswith('|'):
            if head:
                out.append((head, rows))
            head, rows = None, []
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if head is None:
            head = cells
        elif not set(line) <= set('|-: '):
            rows.append((n, cells))
    if head:
        out.append((head, rows))
    return out


skills = {os.path.basename(os.path.dirname(f)) for f in glob.glob(os.path.join(ROOT, 'skills', '*', 'SKILL.md'))}
workflows = {os.path.basename(f)[:-3] for f in glob.glob(os.path.join(ROOT, 'workflows', '*.md'))}
commands = {os.path.basename(f)[:-3] for f in glob.glob(os.path.join(ROOT, 'revit-commands', '*.cs'))} | BUILT_IN

# ---- the matrix itself
kinds = {}
for head, rows in tables(read(os.path.join(DOMAIN, 'drafting-coverage.md'))):
    if head[:2] == [u'Loại', u'Bản vẽ']:
        for n, c in rows:
            row = dict(zip(head, c))
            key = TICK.findall(row[u'Loại'])[0]
            where = 'drafting-coverage.md:%d %s' % (n, key)
            sk, wf = TICK.findall(row['Skill']), TICK.findall(row['Workflow'])
            do, audit = TICK.findall(row[u'Lệnh làm']), TICK.findall(row[u'Lệnh kiểm'])
            level = int(row[u'Mức'])
            for name, known, what in [(x, skills, 'skill') for x in sk] + [(x, workflows, 'workflow') for x in wf] \
                    + [(x, commands, 'command') for x in do + audit]:
                if name not in known:
                    problems.append('%s: %s %s does not exist' % (where, what, name))
            if level >= 2 and not sk:
                problems.append(where + ': level %d needs a skill' % level)
            if level >= 3 and not do:
                problems.append(where + ': level %d needs a do command' % level)
            if level >= 4 and not (audit and wf):
                problems.append(where + ': level 4 needs an audit command and a workflow')
            if not 1 <= level <= 4:
                problems.append(where + ': level 1-4')
            kinds[key] = level
    elif head[:2] == ['#', u'Khâu']:
        for n, c in rows:
            row = dict(zip(head, c))
            for part in row[u'Lệnh'].split(','):
                for name in TICK.findall(part):
                    missing = u'chưa có' in part
                    if missing and name in commands:
                        problems.append('drafting-coverage.md:%d %s exists now: update the row' % (n, name))
                    if not missing and name not in commands:
                        problems.append('drafting-coverage.md:%d command %s does not exist' % (n, name))
            for name in TICK.findall(row['Skill']):
                if name not in skills:
                    problems.append('drafting-coverage.md:%d skill %s does not exist' % (n, name))
if not kinds:
    problems.append('drafting-coverage.md: no kind table read')

# ---- the drawing rows of the core set and of every sheet-set file
per_file, used = [], set()
files = [os.path.join(DOMAIN, 'drafting-sheet-layout.md')] + sorted(glob.glob(os.path.join(DOMAIN, 'drafting-sheet-set-*.md')))
for f in files:
    name, counts = os.path.basename(f), {1: 0, 2: 0, 3: 0, 4: 0}
    for head, rows in tables(read(f)):
        if u'Bản vẽ' not in head or u'Tỷ lệ' not in head:
            continue
        if u'Loại' not in head:
            problems.append('%s: the drawing table has no Loại column' % name)
            continue
        for n, c in rows:
            keys = TICK.findall(dict(zip(head, c)).get(u'Loại', ''))
            if not keys:
                problems.append('%s:%d no Loại' % (name, n))
                continue
            for k in keys:
                if k not in kinds:
                    problems.append('%s:%d Loại %s is not in drafting-coverage.md' % (name, n, k))
            known = [kinds[k] for k in keys if k in kinds]
            used.update(keys)
            if known:
                counts[min(known)] += 1          # a row is only as far as its weakest kind
    per_file.append((name, counts))
for k in sorted(set(kinds) - used):
    problems.append('drafting-coverage.md: kind %s is used by no drawing row' % k)

for p in problems:
    print('FAIL ' + p)
print('%-34s %5s %5s %5s %5s %6s' % ('drawing rows by level', '4', '3', '2', '1', 'rows'))
for name, c in per_file:
    label = 'core set (sheet-layout §4)' if name == 'drafting-sheet-layout.md' else name[len('drafting-sheet-set-'):-3]
    print('%-34s %5d %5d %5d %5d %6d' % (label, c[4], c[3], c[2], c[1], sum(c.values())))
print('coverage: %d kinds (%d at level 4, %d at level 1); %d problem(s)'
      % (len(kinds), sum(1 for v in kinds.values() if v == 4), sum(1 for v in kinds.values() if v == 1), len(problems)))
sys.exit(1 if problems else 0)
