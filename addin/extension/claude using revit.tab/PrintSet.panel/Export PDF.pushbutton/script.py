# -*- coding: utf-8 -*-
"""The drawing list (or the sheets ticked in it) to PDF and / or DWG: one file per sheet.

Which list: the sheet-list schedule open now, else the one picked. Then one glass
window lists its sheets in schedule order, in thin rows under folding groups: all ticked
(or only the ones selected in Revit). Pick by group, by print set, by several numbers /
names (a column pasted from Excel), by dragging or Shift+click: the shared pick list,
lib/pick_list.py. Optionally sort the files
into sub-folders by a sheet parameter (Project Browser grouping suggested), Xuất.
Files are never joined into one file. CAD: pick the model's DWG export setup. Shift+click:
retry the views / sheets left out after a crash (once the model is fixed). All the export
logic is in lib/pdf_export.py + lib/dwg_export.py (shared with Claude's cur_task "pdf_export"
and "dwg_export").
"""
import os

from cur import forms, revit, script

from System.Windows import Visibility

import dwg_export as de
import liquid_glass as lg
import pdf_export as pe
import pick_list as pl
import print_set_sync as ps

doc = revit.doc
uidoc = revit.uidoc


def pick_schedule():
    schedules = ps.sheet_schedules(doc)
    if not schedules:
        lg.alert(u"Model chưa có schedule danh mục bản vẽ (Sheet List).", exitscript=True)
    av = doc.ActiveView.Id.IntegerValue
    for s in schedules:
        if s.Id.IntegerValue == av:
            return s
    last = pe.settings().get("schedule")
    names = sorted((s.Name for s in schedules), key=lambda n: (n != last, n.lower()))
    name = lg.choose(names, u"Xuất PDF / CAD: chọn danh mục bản vẽ", icon=lg.ICON_PDF)
    if not name:
        script.exit()
    return [s for s in schedules if s.Name == name][0]


NO_SETUP = u"Mặc định của Revit (model chưa có template)"
PICK_SET = u"Chọn theo print set…"


class SheetPicker(lg.GlassWindow):
    """Pick the sheets of the drawing list to export (all, or the ones selected in Revit),
    pick the folder, export. self.list (lib/pick_list.py) is the list and holds what is picked."""

    def __init__(self, schedule, sheets, picked_ids):
        lg.GlassWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.result = None
        self.folder = pe.settings().get("folder")
        if self.folder and not os.path.isdir(self.folder):
            self.folder = None
        self.icon.Content = lg.ICON_PDF
        self.subtitle.Text = schedule.Name
        self.header.MouseLeftButtonDown += self.drag
        self.close_btn.Click += lambda s, e: self.Close()
        want = set(picked_ids or [])
        self.sheets = sheets
        self.list = pl.PickList(self, sheets, lambda sh: sh.SheetNumber, lambda sh: sh.Name,
                                on=[not want or sh.Id.IntegerValue in want for sh in sheets],
                                changed=self.refresh)
        # sub-folders: no / Project Browser / one sheet parameter (browser ones suggested)
        self.groups = pe.group_options(doc, sheets)
        self.levels = pe.browser_levels(doc)
        self.paths = {}
        last = pe.settings().get("group")
        pick = 0
        for k, (label, spec, suggested) in enumerate(self.groups):
            self.group_box.Items.Add(label + (u"  · gợi ý" if suggested else u""))
            if spec == last:
                pick = k
        self.group_box.SelectedIndex = pick
        self.group_box.SelectionChanged += self.on_group
        self.browser_paths = {}
        psets = ps.print_sets(doc)
        self.list.bind_sets(self.pset_box, PICK_SET,
                            [(n, lambda n=n: ps.set_sheet_ids(psets[n])) for n in sorted(psets, key=lambda x: x.lower())])
        # what to export: PDF and / or CAD, and the DWG export setup
        self.setups = de.setups(doc)
        for n in self.setups or [NO_SETUP]:
            self.setup_box.Items.Add(n)
        last_setup = de.default_setup(doc)
        self.setup_box.SelectedIndex = self.setups.index(last_setup) if last_setup else 0
        formats = pe.settings().get("formats") or ["pdf"]
        self.fmt_pdf.IsChecked = "pdf" in formats
        self.fmt_dwg.IsChecked = "dwg" in formats
        if not de.accore():
            self.fmt_dwg.IsChecked = False
            self.fmt_dwg.IsEnabled = False
            self.fmt_dwg.ToolTip = u"Cần AutoCAD trên máy để ghép các phần DWG"
            self.fmt_pdf.IsChecked = True
        self.fmt_pdf.Click += self.refresh
        self.fmt_dwg.Click += self.refresh
        self.folder_btn.Click += self.on_folder
        self.export_btn.Click += self.on_export
        self.retry_btn.Click += self.on_retry
        self.show_left_out()
        self.make_groups(fold_if_many=True)
        self.list.show()

    @property
    def group(self):
        return self.groups[max(0, self.group_box.SelectedIndex)][1]

    def on_group(self, sender, args):
        self.paths = {}
        self.make_groups()
        self.list.show()

    def _preview(self, picked):
        """One line: the sub-folders the ticked sheets go to."""
        if not self.group:
            return u"Tất cả file nằm thẳng trong thư mục lưu."
        names = []
        for sh in picked:
            sid = sh.Id.IntegerValue
            if sid not in self.paths:
                self.paths[sid] = u"\\".join(pe.group_path(doc, sh, self.group, self.levels))
            if self.paths[sid] not in names:
                names.append(self.paths[sid])
        shown = u", ".join(names[:6]) + (u", …" if len(names) > 6 else u"")
        return u"{0} thư mục con: {1}".format(len(names), shown) if names else u""

    def make_groups(self, fold_if_many=False):
        """The list's groups: the chosen sub-folders, else the Project Browser's folders; one
        group or none: a flat list."""
        spec = self.group or ({"browser": True} if self.levels else None)
        paths = None
        if spec:
            levels = self.levels
            key = str(sorted(spec.items()))
            paths = []
            for k, sh in enumerate(self.sheets):
                ck = (key, k)
                if ck not in self.browser_paths:
                    self.browser_paths[ck] = u" › ".join(pe.group_path(doc, sh, spec, levels))
                paths.append(self.browser_paths[ck])
        self.list.set_groups(paths, fold_if_many)

    def formats(self):
        return [f for f, b in (("pdf", self.fmt_pdf), ("dwg", self.fmt_dwg)) if b.IsChecked]

    @property
    def setup(self):
        return self.setups[self.setup_box.SelectedIndex] if self.setups else None

    def picked(self):
        return self.list.picked()

    def show_left_out(self):
        """Tell before export which views Revit died on (they go out alone or in tiles)."""
        lo = pe.crash_notes(doc, self.sheets, "pdf") + pe.crash_notes(doc, self.sheets, "dwg")
        if not lo:
            self.warn_box.Visibility = Visibility.Collapsed
            return
        lines = [u"{0}: {1}".format(s.SheetNumber, u", ".join(
            u"{0} (chia {1}x{1} ô)".format(v, n) for v, n in rows) or u"tách từng view")
            for s, rows in lo[:4]] + ([u"…"] if len(lo) > 4 else [])
        self.warn_text.Text = (u"Lần trước Revit văng khi vẽ các view này; lần này tool xuất riêng "
                               u"hoặc chia ô để vẫn ra đủ view:\n" + u"\n".join(lines) +
                               u"\nĐã sửa model? Bấm Thử lại để xuất như bình thường.")
        self.warn_box.Visibility = Visibility.Visible

    def on_retry(self, sender, args):
        pe.forget(doc)
        self.show_left_out()

    def refresh(self, sender=None, args=None):
        picked = self.picked()
        n = len(picked)
        self.big_count.Text = str(n)
        fmts = self.formats()
        self.dwg_box.Visibility = Visibility.Visible if "dwg" in fmts else Visibility.Collapsed
        self.summary.Text = u"sheet · mỗi sheet một file {0}".format(
            u" + ".join(f.upper() for f in fmts) or u"(chưa chọn PDF / CAD)")
        self.folder_text.Text = self.folder or u"Chưa chọn thư mục lưu"
        self.group_preview.Text = self._preview(picked)
        self.export_text.Text = u"Xuất {0} sheet".format(n) if n else u"Chưa chọn sheet"
        self.export_btn.IsEnabled = n > 0 and bool(fmts)

    def on_folder(self, sender=None, args=None):
        f = forms.pick_folder(title=u"Thư mục lưu file")
        if f:
            self.folder = f
            pe.remember_folder(f)
        self.refresh()

    def on_export(self, sender, args):
        if not self.folder:
            self.on_folder()
            if not self.folder:
                return
        self.result = self.picked()
        s = pe.settings()
        s["group"] = self.group
        s["formats"] = self.formats()
        if "dwg" in s["formats"] and self.setup:
            s["dwg_setup"] = self.setup
        pe.save_settings(s)
        self.Close()


if __shiftclick__ and pe.crash_memory(doc):  # noqa: F821 (set by the claude using revit add-in)
    if lg.alert(u"Xoá trí nhớ văng của model này (model đã sửa) để xuất mọi view như bình thường?",
                title=u"Xuất PDF / CAD", icon=lg.ICON_PDF, yes=True, no=True):
        pe.forget(doc)

vs = pick_schedule()
s = pe.settings()
s["schedule"] = vs.Name
pe.save_settings(s)
sheets, total = pe.sheets_of(doc, vs)
if not sheets:
    lg.alert(u"Schedule \"{0}\" không hiện sheet nào in được.".format(vs.Name), exitscript=True)
win = SheetPicker(vs, sheets, [i.IntegerValue for i in uidoc.Selection.GetElementIds()])
win.ShowDialog()
if not win.result:
    script.exit()
incomplete = False
closed = pe.closed_worksets(doc)
if closed:
    # views would print without the elements of these worksets
    if lg.alert(u"Model đang đóng {0} workset, các view sẽ in THIẾU phần tử của chúng:\n  {1}\n\n"
                u"Mở các workset này (Collaborate › Worksets) rồi xuất lại. Chỉ xuất tiếp nếu "
                u"cố ý đóng.".format(len(closed), u"\n  ".join(closed)),
                title=u"Xuất PDF / CAD", icon=lg.ICON_PDF,
                options=[u"Dừng", u"Vẫn xuất (cố ý đóng)"]) != u"Vẫn xuất (cố ý đóng)":
        script.exit()
    incomplete = True
missing = [(sh, ls) for sh, ls in ((sh, pe.unloaded_links(doc, sh)) for sh in win.result) if ls]
if missing:
    # warned before anything is written: the user decides, the export is never refused
    names = sorted(set(n for _, ls in missing for n in ls))
    if lg.alert(u"{0} link chưa load, {1} sheet sẽ in THIẾU phần của link:\n  {2}{3}\n\n"
                u"Sheet: {4}{5}\n\nLoad lại link (Manage › Manage Links) rồi xuất, hoặc vẫn "
                u"xuất nếu không cần link.".format(
                    len(names), len(missing), u"\n  ".join(names[:8]),
                    u"\n  và {0} link khác".format(len(names) - 8) if len(names) > 8 else u"",
                    u", ".join(sh.SheetNumber for sh, _ in missing[:20]),
                    u" và {0} sheet khác".format(len(missing) - 20) if len(missing) > 20 else u""),
                title=u"Xuất PDF / CAD", icon=lg.ICON_PDF,
                options=[u"Dừng", u"Vẫn xuất"]) != u"Vẫn xuất":
        script.exit()
lines = []
for fmt in win.formats():
    out = de.DwgOut(doc, win.setup) if fmt == "dwg" else pe.PdfOut()
    with lg.Progress(u"Xuất {0}".format(out.noun), cancellable=True) as pb:
        def progress(i, n, text):
            pb.title = u"Xuất {0} {1}/{2} · {3}".format(out.noun, i + 1, n, text)
            pb.update_progress(i, n)
        res = pe.run(doc, win.result, win.folder, win.group, progress=progress,
                     cancelled=lambda: pb.cancelled, allow_incomplete=incomplete, out=out)
    lines.append(u"Đã xuất {0}/{1} file {2}{3} vào {4}{5}.".format(
        len(res["done"]), res["total"], out.noun,
        u" ({0} file có sẵn từ lần trước)".format(len(res["skipped"])) if res["skipped"] else u"",
        res["folder"],
        u", chia {0} thư mục con".format(len(res["subfolders"])) if res["subfolders"] else u""))
    if fmt == "dwg":
        lines.append(u"Template CAD: {0}.".format(win.setup or NO_SETUP))
    if res["split"] and fmt != "dwg":
        lines.append(u"Sheet nặng xuất từng phần rồi ghép: " +
                     u", ".join(u"{0} ({1} phần)".format(sh.SheetNumber, n) for sh, n in res["split"]))
    for t in res["learnt"]:
        lines.append(u"Tự tách nhỏ: " + t)
    if res["tiled"]:
        lines.append(u"View từng làm Revit văng, đã xuất từng ô vùng crop rồi ghép (đủ view):")
        lines += [u"  {0}: {1}".format(sh.SheetNumber, u", ".join(
            u"{0} ({1}x{1})".format(v, n) for v, n in rows)) for sh, rows in res["tiled"]]
    if res["warnings"]:
        lines.append(u"Lưu ý: {0} sheet đã xuất thiếu phần của link chưa load ({1}).".format(
            len(res["warnings"]), u", ".join(sh.SheetNumber for sh, _ in res["warnings"][:20]) +
            (u" và {0} sheet khác".format(len(res["warnings"]) - 20)
             if len(res["warnings"]) > 20 else u"")))
    if res["failed"]:
        lines.append(u"Chưa xuất được {0} sheet (không có file thiếu view), bấm lại để thử lại:".format(
            len(res["failed"])))
        lines += [u"  {0}: {1}".format(sh.SheetNumber, e) for sh, e in res["failed"][:8]]
    if res["failed"] or res["learnt"]:
        lines.append(u"Nếu Revit văng giữa chừng: mở lại và bấm Xuất lần nữa, tool nhớ "
                     u"chỗ văng và tự né.")
    if res["stopped"]:
        lines.append(u"Đã dừng an toàn ({0}). Bấm lại để xuất tiếp phần còn lại.".format(res["stopped"]))
        break
choice = lg.alert(u"\n".join(lines), title=u"Xuất PDF / CAD", icon=lg.ICON_PDF,
                  options=[u"Mở thư mục", u"Đóng"])
if choice == u"Mở thư mục":
    os.startfile(res["folder"])
