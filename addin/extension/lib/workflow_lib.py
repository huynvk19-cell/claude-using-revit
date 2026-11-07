# -*- coding: utf-8 -*-
"""What the Workflow windows show: the installed workflows, skills and step cards, read from their files.

Nothing is listed by hand: a new card, skill or workflow in ~/.claude appears on the next read. Also here: the run
state the run_state command writes (run-state.json), a workflow's checklist file, and the results view_develop
leaves in %APPDATA%/claude-using-revit/view-develop/. No Revit and no WPF, so it also runs on plain Python.
"""
import io
import json
import os
import re
import time

HOME = os.path.expanduser(u'~')
APPDATA = os.environ.get('APPDATA') or os.path.join(HOME, 'AppData', 'Roaming')
SKILLS_DIR = os.path.join(HOME, u'.claude', u'skills')
WORKFLOWS_DIR = os.path.join(HOME, u'.claude', u'drafting-domain', u'workflows')
DATA_DIR = os.path.join(APPDATA, u'claude-using-revit')
RUN_STATE = os.path.join(DATA_DIR, u'run-state.json')
RUN_STALE_S = 6 * 3600          # a run that ended without clearing its state
RESULTS_DIR = os.path.join(DATA_DIR, u'view-develop')
INSTANCES_DIR = os.path.join(DATA_DIR, u'instances')

SKILL_PREFIX = u'drafting-'
# shown names and order of the workflows; a workflow not listed here keeps its own heading, at the end
WORKFLOW_NAMES = [(u'floor-plan', u'Mặt bằng'), (u'ceiling-plan', u'Mặt bằng trần'),
                  (u'elevation-section', u'Mặt đứng, mặt cắt'), (u'stair-core', u'Lõi thang'),
                  (u'review-round', u'Vòng comment')]
OTHER = u'Khác'                 # what no workflow reaches
# view_develop's module for a view -> the skills whose job it does (revit-commands/view_develop.cs, Modules)
DEVELOP_SKILLS = {
    u'stair-plan': (u'drafting-stair-plan',),
    u'stair-along': (u'drafting-stair-section-parallel',),
    u'stair-cross': (u'drafting-stair-section-cross',),
    u'plan': (u'drafting-grid-dims', u'drafting-plan-dims', u'drafting-plan-floor-finish'),
    u'ceiling': (u'drafting-grid-dims', u'drafting-ceiling-plan'),
    u'building': (u'drafting-section-general', u'drafting-grid-dims', u'drafting-dims', u'drafting-opening-dims', u'drafting-opening-tags'),
}

SECTION_NAMES = {u'Do': u'Làm', u'Done when': u'Xong khi', u'Skip only if': u'Chỉ bỏ qua khi', u'Never': u'Không được',
                 u'Score (0–10)': u'Điểm (0–10)'}
SECTION_UNITS = {u'Do': u'bước', u'Done when': u'điều kiện', u'Never': u'điều'}
_ITEM = re.compile(r'^(?:- |\d+\. )')
_TICKED = re.compile(r'`([^`]+)`')
_CODE = re.compile(r'\b([A-Z]{1,3})(\d+)\b')
_RANGE = re.compile(r'\b([A-Z]{1,3})(\d+)\s*[–-]\s*(?:\1)?(\d+)\b')


def read(path):
    with io.open(path, encoding='utf-8-sig') as f:
        return f.read().replace(u'\r\n', u'\n')


def short(skill):
    return skill[len(SKILL_PREFIX):] if skill.startswith(SKILL_PREFIX) else skill


def rule_codes(text):
    """The rule codes a line names: TR1–TR8 is TR1, TR2 … TR8."""
    found = set()
    for prefix, a, b in _RANGE.findall(text):
        for n in range(int(a), int(b) + 1):
            found.add(u'{0}{1}'.format(prefix, n))
    for prefix, n in _CODE.findall(text):
        found.add(prefix + n)
    return found


def sections(md):
    """(text before the first ## heading, [(heading, text)])"""
    parts = (u'\n' + md).split(u'\n## ')
    out = []
    for part in parts[1:]:
        head, _, rest = part.partition(u'\n')
        out.append((head.strip(), rest.strip()))
    return parts[0].strip(), out


def section_label(heading):
    return SECTION_NAMES.get(heading, heading)


def section_count(heading, text):
    """The short note beside a closed section: 6 bước, 2 điều kiện…"""
    unit = SECTION_UNITS.get(heading)
    if unit:
        n = len([l for l in text.split(u'\n') if _ITEM.match(l)])
        return u'{0} {1}'.format(n, unit) if n else u''
    if heading.startswith(u'Score') and u'above 7' in text:
        return u'nhận khi > 7'
    return u''


class Card(object):
    def __init__(self, skill, cid, path):
        self.skill = skill
        self.id = cid
        self.path = path
        self.md = read(path)
        first = self.md.split(u'\n', 1)[0]
        self.title = first[2:].strip() if first.startswith(u'# ') else os.path.basename(path)
        self.name = re.sub(r'^\d+\s*·\s*', u'', self.title)
        self.applies = self._line(u'Applies to')
        self.standard = self._line(u'Standard').split(u'·')[0].strip()
        self.tools = _TICKED.findall(self._line(u'Tools'))
        self.intro, self.sections = self._parts()
        word = self.name.split(u' ', 1)[0]
        own = {word} if re.match(r'^[A-Z]{1,3}\d*$', word) else set()
        self.codes = own | rule_codes(self.standard.replace(u'`', u' '))

    def _line(self, label):
        m = re.search(r'\*\*' + label + r'\*\* ([^\n]*)', self.md)
        return m.group(1).strip() if m else u''

    def _parts(self):
        head, parts = sections(self.md)
        intro = [l for l in head.split(u'\n')[1:] if l.strip() and not l.startswith(u'**Skill**') and not l.startswith(u'**Applies to**')]
        return u'\n'.join(intro).strip(), parts


class Skill(object):
    def __init__(self, name, folder):
        self.name = name
        self.folder = folder
        self.path = os.path.join(folder, u'SKILL.md')
        md = read(self.path)
        self.description = u''
        body = md
        if md.startswith(u'---'):
            end = md.find(u'\n---', 3)
            if end > 0:
                m = re.search(r'^description:\s*"?(.*?)"?\s*$', md[:end], re.M)
                self.description = m.group(1) if m else u''
                body = md[end + 4:].strip()
        self.body = body
        self.intro, self.sections = sections(body)
        self.cards = {}
        steps = os.path.join(folder, u'steps')
        if os.path.isdir(steps):
            for f in sorted(os.listdir(steps)):
                if f.endswith(u'.md') and not f.startswith(u'_'):
                    self.cards[f[:2]] = Card(name, f[:2], os.path.join(steps, f))

    def card_ids(self):
        return sorted(self.cards)


class Row(object):
    def __init__(self, number, skill, cards, label, applies):
        self.number = number
        self.skill = skill      # None: the row names no one skill
        self.cards = cards
        self.label = label
        self.applies = applies


class Workflow(object):
    def __init__(self, key, name, path, rows, why):
        self.key = key
        self.name = name
        self.path = path
        self.rows = rows
        self.why = why

    def card_count(self):
        return sum(len(r.cards) for r in self.rows)


def _pick_cards(skill, spec):
    """The card ids a row's Cards column names: all, 00–04, 00, 01. Anything else names no card."""
    ids = skill.card_ids()
    if spec == u'all':
        return ids
    m = re.match(r'^(\d\d)\s*–\s*(\d\d)$', spec)
    if m:
        return [i for i in ids if m.group(1) <= i <= m.group(2)]
    if re.match(r'^\d\d(, \d\d)*$', spec):
        return [i for i in spec.split(u', ') if i in skill.cards]
    return []


def _clean(text):
    return text.replace(u'`', u'').replace(u'**', u'')


def _read_workflow(key, path, skills):
    md = read(path)
    heading = md.split(u'\n', 1)[0]
    names = dict(WORKFLOW_NAMES)
    name = names.get(key) or re.sub(r'^#\s*(Workflow:\s*)?', u'', heading).strip()
    rows = []
    table = [l for l in md.split(u'\n') if l.startswith(u'|')]
    for line in table[2:]:
        cells = [c.strip() for c in line.strip().strip(u'|').split(u'|')]
        if len(cells) < 4:
            continue
        ticked = _TICKED.findall(cells[1])
        skill = skills.get(ticked[0]) if len(ticked) == 1 and cells[1].strip(u'`') == ticked[0] else None
        cards = _pick_cards(skill, cells[2]) if skill is not None else []
        label = u'' if cards or cells[2] == u'—' else _clean(cells[2])
        rows.append(Row(cells[0], skill.name if skill is not None else None, cards, label, _clean(cells[3])))
    m = re.search(r'^Why this order:?\s*(.*)$', md, re.M)
    return Workflow(key, name, path, rows, m.group(1).strip() if m else u'')


class Library(object):
    def __init__(self, skills_dir=None, workflows_dir=None):
        self.skills = {}
        self.workflows = []
        skills_dir = skills_dir or SKILLS_DIR
        workflows_dir = workflows_dir or WORKFLOWS_DIR
        if os.path.isdir(skills_dir):
            for name in sorted(os.listdir(skills_dir)):
                folder = os.path.join(skills_dir, name)
                if name.startswith(SKILL_PREFIX) and os.path.isfile(os.path.join(folder, u'SKILL.md')):
                    self.skills[name] = Skill(name, folder)
        found = {}
        if os.path.isdir(workflows_dir):
            for f in os.listdir(workflows_dir):
                if f.endswith(u'.md'):
                    found[f[:-3]] = os.path.join(workflows_dir, f)
        order = [k for k, _ in WORKFLOW_NAMES if k in found] + sorted(k for k in found if k not in dict(WORKFLOW_NAMES))
        self.workflows = [_read_workflow(k, found[k], self.skills) for k in order]

    def workflow(self, key):
        for w in self.workflows:
            if w.key == key:
                return w
        return None

    def other(self):
        """What no workflow reaches: ([skill names with no row], [(skill name, card id) with no row])."""
        rows = [r for w in self.workflows for r in w.rows if r.skill]
        named = set(r.skill for r in rows)
        reached = set((r.skill, c) for r in rows for c in r.cards)
        whole = set(r.skill for r in rows if not r.cards)
        skills = [s for s in sorted(self.skills) if s not in named]
        cards = [(s, c) for s in sorted(named) for c in self.skills[s].card_ids()
                 if (s, c) not in reached and s not in whole]
        return skills, cards

    def used_in(self, skill, card):
        """The workflows that run this card."""
        return [w for w in self.workflows if any(r.skill == skill and card in r.cards for r in w.rows)]


# ---- run state ------------------------------------------------------------------------------------------------------

def _load_json(path):
    try:
        return json.loads(read(path))
    except Exception:
        return None


def run_status():
    """The step of the running job, from the run_state command: {message, step, workflow, skill, card, checklist}."""
    try:
        if time.time() - os.path.getmtime(RUN_STATE) > RUN_STALE_S:
            return None
    except Exception:
        return None
    st = _load_json(RUN_STATE)
    return st if isinstance(st, dict) else None


_CELL = re.compile(r'^\|\s*(\w+)(?:\.(\d\d))?\s*\|[^|]*\|(.*)\|\s*$')


def checklist_done(path):
    """{(row, card or None)} of a workflow checklist whose cells are all closed: x done, - skipped, n/a."""
    done = set()
    if not path or not os.path.isfile(path):
        return done
    try:
        lines = read(path).split(u'\n')
    except Exception:
        return done
    for line in lines:
        m = _CELL.match(line.strip())
        if not m or m.group(1) == u'#':
            continue
        cells = [c.strip() for c in m.group(3).split(u'|')]
        if cells and all(c.lower() in (u'x', u'-', u'n/a') for c in cells):
            done.add((m.group(1), m.group(2)))
    return done


def _count(value):
    if isinstance(value, dict):
        total = 0
        for v in value.values():
            total += _count(v) if isinstance(v, (dict, list)) else (v if isinstance(v, (int, float)) and not isinstance(v, bool) else 0)
        return int(total)
    if isinstance(value, list):
        return len(value)
    return int(value) if isinstance(value, (int, float)) and not isinstance(value, bool) else 0


class Result(object):
    """The last view_develop of one view."""

    def __init__(self, data, stamp):
        self.stamp = stamp
        self.view = data.get('View') or u'?'
        self.view_id = data.get('viewId')
        self.doc = data.get('doc') or u''
        self.at = data.get('at') or u''
        self.mode = data.get('Mode') or u''
        self.module = data.get('module') or u''
        self.error = data.get('Error')
        self.placed = _count(data.get('Placed'))
        self.ask = _count(data.get('Ask'))
        self.errors = _count(data.get('Errors'))
        over = data.get('Overlaps')
        self.overlaps = _count(over.get('Total')) if isinstance(over, dict) else _count(over)
        left = data.get('Left')
        self.left = {}
        if isinstance(left, dict):
            for k, v in left.items():
                code = k.split(u' ')[0] if re.match(r'^[A-Z]{1,3}\d+( |$)', k) else k
                self.left[code] = self.left.get(code, 0) + (_count(v) or 1)
        self.left_total = sum(self.left.values())

    def label(self):
        kind = u'xem trước' if self.mode == u'preview' else u'đã đặt'
        return u'{0} ({1})'.format(self.view, kind)


def results(limit=40):
    """The newest results first."""
    out = []
    if not os.path.isdir(RESULTS_DIR):
        return out
    for f in os.listdir(RESULTS_DIR):
        path = os.path.join(RESULTS_DIR, f)
        if not f.endswith(u'.json'):
            continue
        data = _load_json(path)
        if isinstance(data, dict):
            try:
                out.append(Result(data, os.path.getmtime(path)))
            except Exception:
                pass
    out.sort(key=lambda r: r.stamp, reverse=True)
    return out[:limit]


def results_stamp():
    """Changes when a result was added, changed or removed."""
    if not os.path.isdir(RESULTS_DIR):
        return 0
    return sum(os.path.getmtime(os.path.join(RESULTS_DIR, f)) for f in os.listdir(RESULTS_DIR))


def develops(skill_name):
    """Does view_develop do part of this skill's job?"""
    return any(skill_name in names for names in DEVELOP_SKILLS.values())


def results_for(skill_name, all_results):
    return [r for r in all_results if skill_name in DEVELOP_SKILLS.get(r.module, ())]


def card_marks(skill, result):
    """How a result reads on a skill's cards: ({card id: open places}, [cards it reports nothing open on], [codes of no card])."""
    todo, matched = {}, set()
    for cid, card in skill.cards.items():
        n = sum(v for k, v in result.left.items() if k in card.codes)
        if n:
            todo[cid] = n
            matched.update(k for k in result.left if k in card.codes)
    clear = [c for c in skill.cards if c not in todo and skill.cards[c].codes]
    return todo, clear, sorted(k for k in result.left if k not in matched)


def instance(pid):
    """This Revit window's line in instances/: role holder or standby."""
    return _load_json(os.path.join(INSTANCES_DIR, u'{0}.json'.format(pid))) or {}


def holder_pid():
    if not os.path.isdir(INSTANCES_DIR):
        return None
    for f in os.listdir(INSTANCES_DIR):
        data = _load_json(os.path.join(INSTANCES_DIR, f))
        if isinstance(data, dict) and data.get('role') == u'holder':
            return data.get('pid')
    return None


def parse_title(title):
    """(model, view or sheet shown, is a sheet) from the Revit window's title; (None, None, False) on the home screen."""
    m = re.search(r'\[(.+?) - (.+)\]\s*$', title or u'')
    if not m:
        return None, None, False
    shown = m.group(2).strip()
    return m.group(1).strip(), shown, shown.startswith(u'Sheet:')


def handoff_text(subject, scope, pid, model, holder):
    """The message for the project chat."""
    lines = [subject, u'Phạm vi: {0}.'.format(scope)]
    where = u'Cửa sổ Revit: PID {0}'.format(pid)
    if model:
        where += u', model {0}'.format(model)
    lines.append(where + u'.')
    if holder and holder != pid:
        lines.append(u'Cửa sổ này chưa giữ cổng 8970 (PID {0} đang giữ): lấy cổng sau khi kiểm tra không có thread khác đang dùng Revit.'.format(holder))
    return u'\n'.join(lines)
