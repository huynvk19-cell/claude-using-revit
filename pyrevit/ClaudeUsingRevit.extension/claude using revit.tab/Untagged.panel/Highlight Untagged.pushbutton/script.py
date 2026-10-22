# -*- coding: utf-8 -*-
"""Orange 50% regions over untagged rooms, doors, windows, walls and floors without a spot elevation."""
from collections import OrderedDict

from pyrevit import forms, revit, script

import untagged_marks as um

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()

SEL = u"Sheet / view đang chọn ({0})"
ACTIVE = u"View đang mở"
PICK_SHEETS = u"Chọn sheet..."
PICK_VIEWS = u"Chọn view..."

picked = um.views_from_ids(doc, list(uidoc.Selection.GetElementIds()))
active = um.views_from_ids(doc, [doc.ActiveView.Id])
ops = []
if picked:
    ops.append(SEL.format(len(picked)))
if active:
    ops.append(ACTIVE)
ops += [PICK_SHEETS, PICK_VIEWS]

switches = OrderedDict()
for k in um.KINDS:
    switches[um.LABELS[k]] = cfg.get_option("kind_" + k, True)

res = forms.CommandSwitchWindow.show(
    ops, switches=switches,
    message=u"Đánh dấu đối tượng chưa tag: bật loại đối tượng, rồi chọn phạm vi")
if not res or not res[0]:
    script.exit()
choice, states = res
kinds = [k for k in um.KINDS if states.get(um.LABELS[k])]
for k in um.KINDS:
    setattr(cfg, "kind_" + k, bool(states.get(um.LABELS[k])))
script.save_config()
if not kinds:
    forms.alert(u"Chưa bật loại đối tượng nào.", exitscript=True)

if choice == ACTIVE:
    views = active
elif choice == PICK_SHEETS:
    sheets = forms.select_sheets(title=u"Chọn sheet", button_name=u"Đánh dấu") or []
    views = um.views_from_ids(doc, [s.Id for s in sheets])
elif choice == PICK_VIEWS:
    sel = forms.select_views(title=u"Chọn view", button_name=u"Đánh dấu",
                             filterfunc=um.is_checkable) or []
    views = um.views_from_ids(doc, [v.Id for v in sel])
else:
    views = picked
if not views:
    forms.alert(u"Không có view mặt bằng / mặt cắt / mặt đứng nào để kiểm tra.",
                exitscript=True)

try:
    report, problems = um.highlight_views(doc, views, kinds)
except Exception as ex:
    forms.alert(u"Không tạo được vùng tô:\n{0}".format(ex), exitscript=True)

total = dict((k, 0) for k in kinds)
lines = []
for name, counts in report:
    for k in kinds:
        total[k] += counts[k]
    parts = [u"{0} {1}".format(um.LABELS[k], counts[k]) for k in kinds if counts[k]]
    lines.append(u"  {0}: {1}".format(name, u", ".join(parts) if parts else u"đủ tag"))
head = u"Chưa tag ({0} view): ".format(len(report)) + u", ".join(
    u"{0} {1}".format(um.LABELS[k], total[k]) for k in kinds)
msg = [head] + lines[:25]
if len(lines) > 25:
    msg.append(u"  ... và {0} view nữa".format(len(lines) - 25))
if problems:
    msg.append(u"Lưu ý ({0}):".format(len(problems)))
    msg += [u"  " + p for p in problems[:10]]
msg.append(u"Xoá vùng tô: nút Remove Untagged.")
forms.alert(u"\n".join(msg))
