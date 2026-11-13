# -*- coding: utf-8 -*-
"""Check the shared pick list's model (addin/extension/lib/pick_model.py) on plain Python 3:
    python addin/tests/pick_model_check.py
Filter, groups, press / drag / Shift ranges, pick all, single pick; and every window XAML of the
extension is well-formed and names the controls the pick list wires."""
import glob
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXT = os.path.join(ROOT, 'addin', 'extension')
sys.path.insert(0, os.path.join(EXT, 'lib'))
import pick_model as pm

labels = [('A-%03d' % i, 'Mat bang tang %d' % i if i < 6 else 'Mat cat %d' % i, '') for i in range(10)]


def model(**kw):
    m = pm.PickModel(labels, **kw)
    m.rebuild()
    return m


def shown(m):
    return [r['no'] for r in m.shown]


# flat list, filter: commas = any, spaces = all words
m = model()
assert len(m.shown) == 10 and not m.picked()
assert pm.parse_terms(u'a-001, A-003\nmat  cat') == [['a-001'], ['a-003'], ['mat', 'cat']]
m.terms = pm.parse_terms('a-001, a-003\nmat cat 7')
m.rebuild()
assert shown(m) == ['A-001', 'A-003', 'A-007'], shown(m)
m.set_matching(True)
assert m.picked() == [1, 3, 7] and m.all_matching_on()
m.terms = []
m.rebuild()
assert not m.all_matching_on()
m.only_on = True
m.rebuild()
assert shown(m) == ['A-001', 'A-003', 'A-007']

# press toggles, drag paints like the first row, moving back gives rows back their state
m = model(on=[k == 4 for k in range(10)])
assert m.press(m.rows[2]) == pm.CHANGED and m.dragging and m.picked() == [2, 4]
assert m.range_to(6) and m.picked() == [2, 3, 4, 5, 6]
assert not m.range_to(6)
assert m.range_to(3) and m.picked() == [2, 3, 4]
m.dragging = False
# Shift+press: the range from the last pressed row, in its state
m.press(m.rows[8], shift=True)
assert m.picked() == [2, 3, 4, 5, 6, 7, 8], m.picked()
m.press(m.rows[2])                     # off, anchor 2
m.dragging = False
m.press(m.rows[5], shift=True)
assert m.picked() == [6, 7, 8], m.picked()

# groups: order of first member, box picks the group, the row folds it, a filter opens it
m = model()
m.set_groups(['B' if k % 2 else 'A' for k in range(10)])
m.rebuild()
assert [r['no'] for r in m.shown if r['kind'] == 'g'] == ['A', 'B'] and len(m.shown) == 12
ga, gb = m.group_rows
assert m.press(ga, 'box') == pm.CHANGED and m.picked() == [0, 2, 4, 6, 8]
m.paint_rows()
assert ga['state'] == '2' and ga['count'] == '5 / 5' and gb['state'] == '0'
m.press(m.rows[1])
m.dragging = False
m.paint_rows()
assert gb['state'] == '1' and gb['count'] == '1 / 5'
assert m.press(ga) == pm.SHOW and m.closed == set(['A'])
m.rebuild()
assert len(m.shown) == 7 and ga['arrow'] == pm.ARROW_SHUT
m.set_matching(False)                  # folded rows too
assert not m.picked()
m.terms = pm.parse_terms('a-002')
m.rebuild()
assert shown(m) == ['A', 'A-002'] and ga['arrow'] == pm.ARROW_OPEN
m.terms = []
m.fold_all()
m.rebuild()
assert shown(m) == ['A', 'B']
m.fold_all(False)
m.set_groups(['same'] * 10)            # one group: a flat list
m.rebuild()
assert not m.group_rows and len(m.shown) == 10 and not m.closed
m.set_groups(None)
m.rebuild()
assert len(m.shown) == 10

# single pick: one at a time, no group box, no pick all
m = model(single=True)
m.set_groups(['B' if k % 2 else 'A' for k in range(10)])
m.rebuild()
m.press(m.rows[3])
m.press(m.rows[5])
assert m.picked() == [5] and not m.dragging
assert m.press(m.group_rows[0], 'box') == pm.SHOW
m.set_matching(True)
assert m.picked() == [5]
m.press(m.rows[5])
assert not m.picked()

# keys: the filter looks in the given text
m = pm.PickModel([('', 'Level 1', 'A-101')], keys=['Level 1 A-101 Mat bang'])
m.terms = pm.parse_terms('a-101 mat')
m.rebuild()
assert len(m.shown) == 1

# XAML: well-formed; a window with the pick list names what PickList wires; the style exists
NS = '{http://schemas.microsoft.com/winfx/2006/xaml}'
files = glob.glob(os.path.join(EXT, '**', '*.xaml'), recursive=True)
users = 0
for f in files:
    root = ET.parse(f).getroot()
    names = set(e.get(NS + 'Name') for e in root.iter())
    if any(e.get('Style') == '{StaticResource PickList}' for e in root.iter()):
        users += 1
        missing = set(['scroller', 'search', 'search_hint', 'pick_all', 'only_on', 'open_all', 'body']) - names
        assert not missing, (f, missing)
theme = ET.parse(os.path.join(EXT, 'lib', 'liquid_glass.xaml')).getroot()
assert any(e.get(NS + 'Key') == 'PickList' for e in theme.iter()), 'PickList style'
assert users >= 2, users
print('pick_model_check: ok ({0} xaml, {1} with the pick list)'.format(len(files), users))
