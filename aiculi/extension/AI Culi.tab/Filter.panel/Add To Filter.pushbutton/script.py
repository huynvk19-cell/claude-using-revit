# -*- coding: utf-8 -*-
"""Elements chosen in the view, those inside links too, into a filter of a view template: hide or no hide.
Nothing selected: pick them in the model or in a link, then Finish."""
from culi import revit, script  # first: loads the WPF assemblies

from System.Windows import TextTrimming, Thickness, Visibility
from System.Windows.Controls import CheckBox, TextBlock

from Autodesk.Revit.Exceptions import OperationCanceledException
from Autodesk.Revit.UI.Selection import ObjectType

import liquid_glass as lg
import view_filter as vf

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()

IN_MODEL, IN_LINK = u"Chọn trong model", u"Chọn trong file link"
NEW_FILTER = u"(Filter mới)"


def pick_elements(whole_links):
    """Nothing to filter by yet: pick in the view."""
    note = u"Đang chọn cả file link, mà filter chỉ lọc theo từng phần tử trong đó.\n" if whole_links else u""
    how = lg.choose([IN_MODEL, IN_LINK], u"Thêm vào filter", note + u"Chọn phần tử ở đâu? Chọn xong bấm Finish.")
    if how is None:
        return []
    kind = ObjectType.LinkedElement if how == IN_LINK else ObjectType.Element
    try:
        refs = uidoc.Selection.PickObjects(kind, u"Chọn phần tử cần đưa vào filter rồi bấm Finish")
    except OperationCanceledException:
        return []
    return vf.from_references(doc, refs)


class FilterWindow(lg.GlassWindow):
    def __init__(self, elements, opts):
        lg.GlassWindow.__init__(self, script.get_bundle_file("ui.xaml"))
        self.elements, self.opts = elements, opts
        self.result = None
        self.auto_name = u""
        self.header.MouseLeftButtonDown += lambda s, e: self.DragMove()
        self.close_btn.Click += lambda s, e: self.Close()
        self.apply_btn.Click += self.apply

        cats = u", ".join(opts["categories"][:3]) + (u"…" if len(opts["categories"]) > 3 else u"")
        count = u"{0} phần tử".format(opts["elements"])
        if opts["linked"]:
            count += u" ({0} trong file link)".format(opts["linked"])
        self.summary.Text = u"{0} · {1}".format(count, cats)
        if opts["skipped"]:
            self.warn(u"Bỏ qua, category không dùng được cho filter: " + u", ".join(
                u"{0} ({1})".format(n, c) for n, c in sorted(opts["skipped"].items())))

        self.checks = []
        for i, c in enumerate(opts["choices"]):
            shown = u", ".join(c["values"][:6]) + (u" …" if len(c["values"]) > 6 else u"")
            label = TextBlock()
            label.Text = u"{0}:  {1}".format(c["name"], shown)
            label.TextTrimming = TextTrimming.CharacterEllipsis
            label.ToolTip = u"\n".join(c["values"][:40])
            box = CheckBox()
            box.Content = label
            box.Margin = Thickness(0, 2, 0, 2)
            box.IsChecked = i == 0
            box.Checked += self.refresh_name
            box.Unchecked += self.refresh_name
            self.param_list.Children.Add(box)
            self.checks.append((box, c))

        targets = opts["targets"]
        self.target_box.ItemsSource = [t["name"] + (u"  (của {0})".format(t["lastChangedBy"]) if t["others"] else u"")
                                       for t in targets]
        last = cfg.get_option("template", u"")
        start = next((k for k, t in enumerate(targets) if t["id"] == opts["target"]), None)
        if start is None:
            start = next((k for k, t in enumerate(targets) if t["name"] == last), 0 if targets else -1)
        self.target_box.SelectedIndex = start

        self.filter_box.ItemsSource = [NEW_FILTER] + [f["name"] for f in opts["filters"]]
        self.filter_box.SelectedIndex = 0
        self.filter_box.SelectionChanged += self.filter_changed
        hide = cfg.get_option("hide", u"1") == u"1"
        self.hide_rb.IsChecked = hide
        self.show_rb.IsChecked = not hide
        self.refresh_name(None, None)

    def warn(self, text):
        self.warn_text.Text = text
        self.warn_box.Visibility = Visibility.Visible

    def chosen(self):
        return [c for box, c in self.checks if box.IsChecked]

    def refresh_name(self, sender, args):
        """The suggested name follows the ticks until the name is typed over."""
        now = self.name_box.Text or u""
        if now == self.auto_name:
            picked = self.chosen()
            self.auto_name = vf.default_name(self.opts["categories"], picked[0] if picked else None)
            self.name_box.Text = self.auto_name

    def filter_changed(self, sender, args):
        new = self.filter_box.SelectedIndex <= 0
        self.name_box.Visibility = Visibility.Visible if new else Visibility.Collapsed

    def filter_name(self):
        if self.filter_box.SelectedIndex > 0:
            return self.filter_box.SelectedItem
        return (self.name_box.Text or u"").strip()

    def apply(self, sender, args):
        picked, name = self.chosen(), self.filter_name()
        if not picked:
            return self.warn(u"Chọn ít nhất một tham số để lọc.")
        if self.target_box.SelectedIndex < 0:
            return self.warn(u"Model này chưa có view template nhận filter.")
        if not name:
            return self.warn(u"Đặt tên filter.")
        target = self.opts["targets"][self.target_box.SelectedIndex]
        hide = bool(self.hide_rb.IsChecked)
        params = [c["id"] for c in picked]
        try:
            res = vf.add(doc, self.elements, params, target["id"], name, hide)
            if res.get("blocked"):
                msg = u"Của người khác:\n{0}\n\nVẫn sửa?".format(u"\n".join(res["blocked"]))
                if not lg.alert(msg, yes=True, no=True):
                    return
                res = vf.add(doc, self.elements, params, target["id"], name, hide, force=True)
        except Exception as ex:
            return self.warn(u"Chưa thêm được: {0}".format(ex))
        if not target["id"] == self.opts["target"]:
            cfg.template = target["name"]
        cfg.hide = u"1" if hide else u"0"
        script.save_config()
        self.result = res
        self.Close()


elements, whole = vf.selected(doc, uidoc)
if not elements:
    elements = pick_elements(whole)
if not elements:
    script.exit()

try:
    opts = vf.options(doc, elements)
except ValueError as ex:
    lg.alert(u"{0}".format(ex), exitscript=True)
if not opts["choices"]:
    lg.alert(u"Các phần tử này không có tham số chung nào lọc được.", exitscript=True)

win = FilterWindow(elements, opts)
win.ShowDialog()
res = win.result
if res:
    lines = [u"{0} filter {1}".format(u"Đã tạo" if res["newFilter"] else u"Đã mở rộng", res["filter"]),
             u"{0} · {1}".format(res["template"], u"Hide" if res["hide"] else u"No hide")]
    if res.get("linked"):
        lines.append(u"Phần tử trong link chỉ theo filter ở view để link By Host View.")
    if res.get("notControlled"):
        lines.append(u"Template này không điều khiển Filters (V/G Overrides Filter chưa tick Include) nên view dùng nó chưa đổi.")
    lg.alert(u"\n".join(lines))
