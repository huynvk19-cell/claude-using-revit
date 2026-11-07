# -*- coding: utf-8 -*-
"""The drawing list (or the sheets ticked in it) to PDF and / or DWG: one file per sheet.

Which list: the sheet-list schedule open now, else the one picked. Then one glass
window lists its sheets in schedule order, in thin rows under folding groups: all ticked
(or only the ones selected in Revit). Pick by group, by print set, by several numbers /
names (a column pasted from Excel), by dragging or Shift+click. Optionally sort the files
into sub-folders by a sheet parameter (Project Browser grouping suggested), Xuất.
Files are never joined into one file. CAD: pick the model's DWG export setup. Shift+click:
retry the views / sheets left out after a crash (once the model is fixed). All the export
logic is in lib/pdf_export.py + lib/dwg_export.py (shared with Claude's cur_task "pdf_export"
and "dwg_export").
"""
import os
import re

from cur import forms, revit, script

from System import Double, Object, String
from System.Collections.Generic import Dictionary, List
from System.Windows import FontWeights, FrameworkElement, GridLength, Size, Visibility
from System.Windows.Controls import ListBoxItem, TextBlock
from System.Windows.Input import Key, Keyboard, ModifierKeys, Mouse, MouseButtonState
from System.Windows.Media import VisualTreeHelper

import dwg_export as de
import liquid_glass as lg
import pdf_export as pe
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
OPEN_UP_TO = 60                     # more sheets than this: the groups start folded
ARROW_OPEN, ARROW_SHUT = u"\uE70D", u"\uE76C"


class SheetPicker(lg.GlassWindow):
    """Pick the sheets of the drawing list to export (all, or the ones selected in Revit),
    pick the folder, export. self.on holds what is picked; rows are built for what is
    shown and only those on screen get drawn (ListBox virtualizing), so 1000 sheets are fine."""

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
        self.on = [not want or sh.Id.IntegerValue in want for sh in sheets]
        self.keys = [(sh.SheetNumber + u" " + sh.Name).lower() for sh in sheets]
        self.terms, self.match, self.shown, self.where = [], set(range(len(sheets))), [], {}
        self.closed = set()
        self.anchor, self.dragging, self.paint, self.before, self.last = None, False, True, [], None
        self.Resources["NoWidth"] = self._no_width(sheets)
        self.scroller.PreviewMouseLeftButtonDown += self._press
        self.scroller.PreviewMouseMove += self._move
        self.PreviewMouseLeftButtonUp += self._release
        self.PreviewKeyDown += self._key
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
        self.browser_paths, self.group_rows, self.members = {}, [], {}
        self.rows = [self._sheet_row(k, sh) for k, sh in enumerate(sheets)]
        self.psets = ps.print_sets(doc)
        for n in [PICK_SET] + sorted(self.psets, key=lambda x: x.lower()):
            self.pset_box.Items.Add(n)
        self.pset_box.SelectedIndex = 0
        self.pset_box.SelectionChanged += self.on_pset
        self.only_on.Click += self.show
        self.open_all.Click += self.on_open_all
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
        self.search.TextChanged += self.on_search
        self.pick_all.Click += self.on_all
        self.folder_btn.Click += self.on_folder
        self.export_btn.Click += self.on_export
        self.retry_btn.Click += self.on_retry
        self.show_left_out()
        self.make_groups()
        if len(sheets) > OPEN_UP_TO:
            self.closed = set(g["no"] for g in self.group_rows)
        self.open_all.IsChecked = not self.closed
        self.show()

    @property
    def group(self):
        return self.groups[max(0, self.group_box.SelectedIndex)][1]

    def on_group(self, sender, args):
        self.paths = {}
        self.make_groups()
        self.show()

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

    def _no_width(self, sheets):
        """Width of the number column: names line up in thin rows."""
        tb = TextBlock()
        tb.Text = max((sh.SheetNumber for sh in sheets), key=len) if sheets else u""
        tb.FontSize = 12
        tb.FontWeight = FontWeights.SemiBold
        tb.Measure(Size(Double.PositiveInfinity, Double.PositiveInfinity))
        return tb.DesiredSize.Width + 8

    @staticmethod
    def _dict(**kw):
        d = Dictionary[String, Object]()
        for k, v in kw.items():
            d[k] = v
        return d

    def _sheet_row(self, k, sh):
        return self._dict(kind="s", k=k, no=sh.SheetNumber, name=sh.Name, count=u"", arrow=u"",
                          indent=GridLength(6), state="2" if self.on[k] else "0")

    def make_groups(self):
        """The list's groups: the chosen sub-folders, else the Project Browser's folders; one
        group or none: a flat list."""
        spec = self.group or ({"browser": True} if self.levels else None)
        self.members, self.group_rows = {}, []
        if spec:
            levels = self.levels
            key = str(sorted(spec.items()))
            for k, sh in enumerate(self.sheets):
                ck = (key, k)
                if ck not in self.browser_paths:
                    self.browser_paths[ck] = u" › ".join(pe.group_path(doc, sh, spec, levels))
                self.members.setdefault(self.browser_paths[ck], []).append(k)
        if len(self.members) < 2:
            self.members = {}
        seen = sorted(self.members, key=lambda g: self.members[g][0])
        self.group_rows = [self._dict(kind="g", k=-1, no=g, name=u"", count=u"", arrow=ARROW_OPEN,
                                      indent=GridLength(18), state="0") for g in seen]
        self.open_all.Visibility = Visibility.Visible if self.group_rows else Visibility.Collapsed

    def formats(self):
        return [f for f, b in (("pdf", self.fmt_pdf), ("dwg", self.fmt_dwg)) if b.IsChecked]

    @property
    def setup(self):
        return self.setups[self.setup_box.SelectedIndex] if self.setups else None

    def picked(self):
        return [sh for k, sh in enumerate(self.sheets) if self.on[k]]

    def _matches(self, k):
        if self.only_on.IsChecked and not self.on[k]:
            return False
        if not self.terms:
            return True
        text = self.keys[k]
        return any(all(w in text for w in term) for term in self.terms)

    def show(self, sender=None, args=None):
        """Rebuild what the list shows, from the filter and the folded groups."""
        self.match = set(k for k in range(len(self.sheets)) if self._matches(k))
        shown = []
        filtering = bool(self.terms or self.only_on.IsChecked)
        if self.group_rows:
            for g in self.group_rows:
                hits = [k for k in self.members[g["no"]] if k in self.match]
                if not hits:
                    continue
                shown.append(g)
                if filtering or g["no"] not in self.closed:
                    shown += [self.rows[k] for k in hits]
        else:
            shown = [self.rows[k] for k in sorted(self.match)]
        self.shown = shown
        self.where = dict((r["k"], i) for i, r in enumerate(shown) if r["kind"] == "s")
        self._paint()
        self.scroller.ItemsSource = List[Object](shown)
        self.refresh()

    def _paint(self):
        for k, r in enumerate(self.rows):
            r["state"] = "2" if self.on[k] else "0"
        filtering = bool(self.terms or self.only_on.IsChecked)
        for g in self.group_rows:
            m = self.members[g["no"]]
            n = len([k for k in m if self.on[k]])
            g["state"] = "2" if n == len(m) else "1" if n else "0"
            g["count"] = u"{0} / {1}".format(n, len(m))
            g["arrow"] = ARROW_OPEN if filtering or g["no"] not in self.closed else ARROW_SHUT

    def changed(self):
        """What is picked changed: redraw the rows on screen, recount."""
        if self.only_on.IsChecked and not self.dragging:
            self.show()
            return
        self._paint()
        self.scroller.Items.Refresh()
        self.refresh()

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

    # ---- Photos-style picking: press = toggle and start a drag; dragging over rows sets
    # them all like the first one (moving back gives rows back their state); Shift+click
    # = the whole range from the last clicked row; Ctrl+A = every sheet the filter lets
    # through. A group's box picks the whole group, the rest of its row folds it.

    @staticmethod
    def _mod(m):
        return (Keyboard.Modifiers & m) == m

    @staticmethod
    def _held():
        return Mouse.LeftButton == MouseButtonState.Pressed

    def _hit(self, source):
        """(row under this element, its tag "box" / "arrow" / None); no row on the scroll bar."""
        tag = None
        el = source
        while el is not None and not isinstance(el, ListBoxItem):
            if tag is None and isinstance(el, FrameworkElement) and el.Tag in ("box", "arrow"):
                tag = el.Tag
            try:
                el = VisualTreeHelper.GetParent(el)
            except Exception:
                el = None
        # a recycled row waiting for use holds no row
        row = el.Content if el is not None else None
        return (row, tag) if isinstance(row, Dictionary[String, Object]) else (None, None)

    def _press(self, sender, e):
        row, tag = self._hit(e.OriginalSource)
        if row is None:
            return
        e.Handled = True
        if row["kind"] == "g":
            m = self.members[row["no"]]
            if tag == "box":
                v = not all(self.on[k] for k in m)
                for k in m:
                    self.on[k] = v
                self.changed()
            else:
                self.closed.symmetric_difference_update([row["no"]])
                self.open_all.IsChecked = not self.closed
                self.show()
            return
        k = row["k"]
        if self._mod(ModifierKeys.Shift) and self.anchor in self.where:
            self.before = list(self.on)
            self.paint = self.on[self.anchor]
            self._range(k)
            return
        self.anchor = k
        self.paint = not self.on[k]
        self.before = list(self.on)
        self.dragging = True
        self.last = None
        self._range(k)

    def _range(self, k):
        """Rows shown from the anchor to k take the first row's state, others go back."""
        if k == self.last:
            return
        self.last = k
        lo, hi = sorted((self.where[self.anchor], self.where[k]))
        self.on = list(self.before)
        for r in self.shown[lo:hi + 1]:
            if r["kind"] == "s":
                self.on[r["k"]] = self.paint
        self.changed()

    def _move(self, sender, e):
        if not self.dragging:
            return
        if not self._held():
            self.dragging = False
            return
        row, _ = self._hit(e.OriginalSource)
        if row is not None and row["kind"] == "s" and row["k"] in self.where:
            self._range(row["k"])
            i = self.where[row["k"]]
            y = e.GetPosition(self.scroller).Y        # near the edges the list scrolls on
            if y < 24 and i > 0:
                self.scroller.ScrollIntoView(self.shown[i - 1])
            elif y > self.scroller.ActualHeight - 24 and i + 1 < len(self.shown):
                self.scroller.ScrollIntoView(self.shown[i + 1])

    def _release(self, sender, e):
        was = self.dragging
        self.dragging = False
        if was and self.only_on.IsChecked:
            self.show()

    def _key(self, sender, e):
        if e.Key == Key.Return and self.search.IsKeyboardFocusWithin:
            e.Handled = True        # the box takes pasted lines, Enter adds none
        if e.Key == Key.A and self._mod(ModifierKeys.Control) \
                and not self.search.IsKeyboardFocusWithin:
            for k in self.match:
                self.on[k] = True
            self.changed()
            e.Handled = True

    def on_pset(self, sender, args):
        name = self.pset_box.SelectedItem
        if name == PICK_SET or name not in self.psets:
            return
        ids = ps.set_sheet_ids(self.psets[name])
        self.on = [sh.Id.IntegerValue in ids for sh in self.sheets]
        self.show()

    def on_open_all(self, sender, args):
        self.closed = set() if self.open_all.IsChecked else set(g["no"] for g in self.group_rows)
        self.show()

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
        self.pick_all.IsChecked = bool(self.match) and all(self.on[k] for k in self.match)

    def on_search(self, sender, args):
        text = (self.search.Text or u"").lower()
        self.search_hint.Visibility = Visibility.Collapsed if text.strip() else Visibility.Visible
        # commas / lines: any of them; spaces in one: all of its words
        self.terms = [t.split() for t in re.split(u"[,;\\r\\n\\t]+", text) if t.strip()]
        self.show()

    def on_all(self, sender, args):
        for k in self.match:
            self.on[k] = bool(self.pick_all.IsChecked)
        self.changed()

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
