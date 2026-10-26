# -*- coding: utf-8 -*-
"""One click: the drawing list (or the sheets picked in it) to one PDF.

Which sheets: the sheet-list schedule open now (rows selected = only those sheets),
else the sheets selected in the Project Browser within the chosen schedule, else the
whole chosen schedule. Folder asked once (Shift+click to change). All the export
logic is in lib/pdf_export.py (shared with Claude's pyrevit_task "pdf_export").
"""
import os

from pyrevit import forms, revit, script

import pdf_export as pe
import print_set_sync as ps

doc = revit.doc
uidoc = revit.uidoc


def pick_schedule():
    schedules = ps.sheet_schedules(doc)
    if not schedules:
        forms.alert(u"Model chưa có schedule danh mục bản vẽ (Sheet List).", exitscript=True)
    av = doc.ActiveView.Id.IntegerValue
    for s in schedules:
        if s.Id.IntegerValue == av:
            return s
    last = pe.settings().get("schedule")
    names = sorted((s.Name for s in schedules), key=lambda n: (n != last, n.lower()))
    name = forms.SelectFromList.show(names, title=u"Xuất PDF: chọn danh mục bản vẽ",
                                     button_name=u"Xuất PDF", multiselect=False)
    if not name:
        script.exit()
    return [s for s in schedules if s.Name == name][0]


def folder():
    f = pe.settings().get("folder")
    if f and os.path.isdir(f) and not __shiftclick__:  # noqa: F821 (pyRevit global)
        return f
    f = forms.pick_folder(title=u"Thư mục lưu PDF")
    if not f:
        script.exit()
    pe.remember_folder(f)
    return f


vs = pick_schedule()
s = pe.settings()
s["schedule"] = vs.Name
pe.save_settings(s)
picked = [i.IntegerValue for i in uidoc.Selection.GetElementIds()]
sheets, total = pe.sheets_of(doc, vs, picked)
if picked and not sheets:
    sheets, total = pe.sheets_of(doc, vs)
if not sheets:
    forms.alert(u"Schedule \"{0}\" không hiện sheet nào in được.".format(vs.Name), exitscript=True)
base = folder()
title = pe.title_for(vs, sheets, total)

with forms.ProgressBar(title=u"Xuất PDF", cancellable=True) as pb:
    def progress(i, n, text):
        pb.title = u"Xuất PDF {0}/{1} · {2}".format(i + 1, n, text)
        pb.update_progress(i, n)
    res = pe.run(doc, sheets, base, title, progress=progress, cancelled=lambda: pb.cancelled)

lines = []
lines.append(u"Đã xuất {0}/{1} sheet{2}.".format(
    len(res["done"]), res["total"],
    u" ({0} sheet có sẵn từ lần trước)".format(len(res["skipped"])) if res["skipped"] else u""))
if res["split"]:
    lines.append(u"Sheet nặng xuất từng phần rồi ghép: " +
                 u", ".join(u"{0} ({1} phần)".format(sh.SheetNumber, n) for sh, n in res["split"]))
for t in res["learnt"]:
    lines.append(u"Lần trước Revit văng: " + t)
if res["missing_views"]:
    lines.append(u"View làm Revit văng cả khi xuất raster, đã bỏ khỏi PDF (cần sửa trong model):")
    lines += [u"  {0}: {1}".format(sh.SheetNumber, u", ".join(v)) for sh, v in res["missing_views"]]
if res["failed"]:
    lines.append(u"Lỗi {0} sheet, bấm lại để thử lại:".format(len(res["failed"])))
    lines += [u"  {0}: {1}".format(sh.SheetNumber, e) for sh, e in res["failed"][:8]]
if res["failed"] or res["learnt"]:
    lines.append(u"Nếu Revit văng giữa chừng: mở lại và bấm Xuất PDF lần nữa, tool nhớ "
                 u"chỗ văng và tự né.")
if res["stopped"]:
    lines.append(u"Đã dừng an toàn ({0}). Bấm lại để xuất tiếp phần còn lại.".format(res["stopped"]))
if res["join_error"]:
    lines.append(u"Chưa ghép được file chung: {0}. Các sheet lẻ vẫn ở thư mục sheets.".format(
        res["join_error"]))
target = res["joined"] or res["folder"]
if res["joined"]:
    lines.append(u"File: " + os.path.basename(res["joined"]))
choice = forms.alert(u"\n".join(lines), title=u"Xuất PDF",
                     options=[u"Mở PDF" if res["joined"] else u"Mở thư mục", u"Đóng"])
if choice and choice.startswith(u"Mở"):
    os.startfile(target)
