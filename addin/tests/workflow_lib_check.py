# -*- coding: utf-8 -*-
"""Check addin/extension/lib/workflow_lib.py against this repo's own files, on plain Python 3:
    python addin/tests/workflow_lib_check.py
Every workflow row names a known skill and existing cards, and what no workflow reaches is listed under Khác."""
import json
import os
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, 'addin', 'extension', 'lib'))
import workflow_lib as lib

library = lib.Library(os.path.join(ROOT, 'skills'), os.path.join(ROOT, 'workflows'))
assert library.workflows, 'no workflow read'
assert [w.key for w in library.workflows][:5] == [k for k, _ in lib.WORKFLOW_NAMES], 'order of the workflows'
for w in library.workflows:
    assert w.rows, w.key + ': no rows'
    for r in w.rows:
        if r.skill:
            assert r.skill in library.skills, r.skill
            for c in r.cards:
                assert c in library.skills[r.skill].cards, (r.skill, c)
for s in library.skills.values():
    for c in s.cards.values():
        assert c.title and c.sections, (s.name, c.id)
reached = set((r.skill, c) for w in library.workflows for r in w.rows if r.skill for c in r.cards)
skills, cards = library.other()
for s in library.skills.values():
    for c in s.card_ids():
        named = any(r.skill == s.name and not r.cards for w in library.workflows for r in w.rows)
        assert (s.name, c) in reached or (s.name, c) in cards or s.name in skills or named, (s.name, c)

assert lib.rule_codes(u'`x.md` §2 TR1–TR3') == {'TR1', 'TR2', 'TR3'}
assert lib.parse_title(u'Autodesk Revit 2023.1 - [M.rvt - Sheet: A - B]') == (u'M.rvt', u'Sheet: A - B', True)
assert lib.parse_title(u'Autodesk Revit 2023.1 - [Home]')[0] is None

stair = library.skills['drafting-stair-plan']
assert 'SA1' in stair.cards['02'].codes and lib.develops(stair.name)
for names in lib.DEVELOP_SKILLS.values():
    for name in names:
        assert name in library.skills, name
result = lib.Result({'View': 'V', 'Mode': 'apply', 'Placed': {'tags': 3, 'dims': 2}, 'Ask': ['a'], 'Errors': [], 'Left': {'SA1': 2, 'Q dọc': 1},
                     'Overlaps': {'Total': 4}}, 0)
assert (result.placed, result.ask, result.overlaps, result.left_total) == (5, 1, 4, 3)
todo, clear, loose = lib.card_marks(stair, result)
assert todo == {'02': 2} and 'Q dọc' in loose and '02' not in clear

with tempfile.TemporaryDirectory() as d:
    path = os.path.join(d, 'c.md')
    with open(path, 'w', encoding='utf-8') as f:
        f.write('| # | Item | 1 | 2 |\n|---|---|---|---|\n| 3.02 | tag | x | - |\n| 3.03 | spot | x |  |\n| 6 | check | n/a | x |\n')
    assert lib.checklist_done(path) == {('3', '02'), ('6', None)}

print('ok: {0} workflows, {1} skills, {2} cards, other: {3} skills + {4} cards'.format(
    len(library.workflows), len(library.skills), sum(len(s.cards) for s in library.skills.values()), len(skills), len(cards)))
