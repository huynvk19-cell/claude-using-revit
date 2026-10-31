# -*- coding: utf-8 -*-
"""What slows Revit down: machine, model, the active view, and what to do about it. Read only."""
from pyrevit import revit, script

from Autodesk.Revit.DB import ElementId

import liquid_glass as lg
import tasks

doc, uidoc = revit.doc, revit.uidoc
out = script.get_output()

try:
    r = tasks.run("speed_check", {"detail": True}, doc, uidoc)
except Exception as ex:
    lg.alert(u"{0}".format(ex), title=u"Kiểm tra tốc độ", exitscript=True)


def link(i):
    return out.linkify(ElementId(int(i)))


out.set_title(u"Kiểm tra tốc độ")
out.print_md(u"## Nên làm")
for i, t in enumerate(r["tips"] or [u"Không thấy gì đáng kể."], 1):
    out.print_md(u"{0}. {1}".format(i, t))

m = r.get("machine")
if m:
    out.print_md(u"## Máy")
    out.print_md(u"{0} · RAM {1:,} MB (trống {2:,} MB)".format(m["revit"], m["ramMB"], m["freeMB"]))
    out.print_table([[g["name"], g["vramMB"], g["revitUsesMB"]] for g in m["gpus"]],
                    columns=[u"Card màn hình", u"VRAM MB", u"Revit đang dùng MB"])
    if m["revitIni"]:
        out.print_md(u"Revit.ini [Graphics]: " + u", ".join(
            u"{0}={1}".format(k, v) for k, v in sorted(m["revitIni"].items())))

v = r.get("view")
if v:
    out.print_md(u"## View '{0}' {1}".format(v["view"], link(v["id"])))
    rows = [[u"Type", v["type"]], [u"View Template", v.get("template") or u"<None>"],
            [u"Detail Level", v.get("detail", u"-")], [u"Visual Style", v.get("style", u"-")],
            [u"Anti-aliasing", v.get("antiAliasing", u"-")],
            [u"Silhouettes", v.get("silhouettes", u"-")], [u"Sketchy Lines", v.get("sketchy", u"-")],
            [u"Crop", v.get("cropActive", u"-")], [u"Phần tử đang hiện", v["elementsShown"]],
            [u"Chế độ nhẹ", v["light"]]]
    if "farClip" in v:
        rows.append([u"Far Clipping", u"{0} mm".format(v["farClipMm"]) if v["farClip"] else u"No clip"])
    if "sectionBox" in v:
        rows.append([u"Section Box", v["sectionBox"]])
    out.print_table(rows, columns=[u"", u""])
    out.print_md(u"Category nhiều nhất: " + u" · ".join(v["topCategories"]))
    if v["linksShown"]:
        out.print_md(u"Link Revit đang hiện: " + u", ".join(v["linksShown"]))
    if v["cadShown"]:
        out.print_md(u"CAD đang hiện: " + u", ".join(v["cadShown"]))

md = r.get("model")
if md:
    out.print_md(u"## Model '{0}'".format(md["model"]))
    ws = md.get("worksets")
    out.print_md(u"{0}{1:,} phần tử · {2:,} warnings · {3} view · {4} sheet · {5} family{6}".format(
        u"{0} MB · ".format(md["fileMB"]) if "fileMB" in md else u"", md["elements"],
        md["warnings"], md["views"], md["sheets"], md["families"],
        u" · worksets mở {0}/{1}".format(ws["open"], ws["total"]) if ws else u""))
    if md["cad"]:
        out.print_md(u"### CAD ({0})".format(len(md["cad"])))
        out.print_table([[link(c["id"]), c["name"], u"link" if c["linked"] else u"import",
                          c["view"] or u"model (3D)"] for c in md["cad"]],
                        columns=[u"Id", u"File", u"Kiểu", u"View"])
    if md["inPlace"]:
        out.print_md(u"### In-place family ({0})".format(len(md["inPlace"])))
        out.print_table([[f["family"], f["category"], f["instances"]] for f in md["inPlace"]],
                        columns=[u"Family", u"Category", u"Số instance"])
    if md["modelGroups"]:
        out.print_md(u"### Model group (nhiều nhất)")
        out.print_table([[g["group"], g["instances"], g["members"]] for g in md["modelGroups"]],
                        columns=[u"Group", u"Số instance", u"Thành phần"])
    if md["links"]:
        out.print_md(u"### Link Revit")
        out.print_table([[l["link"], u"loaded" if l["loaded"] else u"unloaded", l["instances"]]
                         for l in md["links"]], columns=[u"Link", u"Trạng thái", u"Instance"])
    out.print_md(u"Raster image: {0} · Point cloud: {1}".format(md["rasterImages"], md["pointClouds"]))
    if md["lightViews"]:
        out.print_md(u"Đang ở chế độ nhẹ: " + u", ".join(md["lightViews"]))
