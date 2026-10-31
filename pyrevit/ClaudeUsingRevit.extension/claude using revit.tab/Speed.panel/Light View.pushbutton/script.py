# -*- coding: utf-8 -*-
"""Light mode on the active view: on, or back as it was (Shift+click: more choices)."""
from pyrevit import EXEC_PARAMS, forms, revit, script

import liquid_glass as lg
import tasks
import view_speed as vsp

doc, uidoc = revit.doc, revit.uidoc


def say(title, lines):
    try:
        forms.show_balloon(title, u"\n".join(lines))
    except Exception:
        lg.alert(u"\n".join(lines), title=title)


def run(args):
    try:
        return tasks.run("view_light", args, doc, uidoc)
    except Exception as ex:
        lg.alert(u"{0}".format(ex), title=u"Chế độ nhẹ", exitscript=True)


args = {}
if EXEC_PARAMS.config_mode:
    light = vsp.light_views(doc)
    LINKS = u"Bật chế độ nhẹ, ẩn tạm cả link Revit"
    ALL = u"Trả về tất cả view đang nhẹ ({0})".format(len(light))
    pick = lg.choose([LINKS, ALL] if light else [LINKS], u"Chế độ nhẹ",
                     u"\n".join(e.get("name") for e in light.values()) or None)
    if pick is None:
        script.exit()
    if pick == ALL:
        r = tasks.run("view_light_restore_all", {}, doc, uidoc)
        say(u"Đã trả về", [u"{0} view".format(len(r.get("restored", [])))] + r.get("failed", []))
        script.exit()
    args["links"] = True

r = run(args)
if "restored" in r:
    lines = [u"View đã trả về như cũ."] + r["failed"]
    if r["tempHideLeftOn"]:
        lines.append(u"Temporary Hide của bạn có từ trước vẫn giữ: Reset Temporary Hide/Isolate "
                     u"khi xong.")
    say(u"Chế độ nhẹ: tắt", lines)
else:
    lines = [u"Đổi: {0}".format(u", ".join(c.split(u":")[0] for c in r["change"]) or u"không có")]
    hid = r["hideElements"] + r["hideCategories"]
    if hid:
        lines.append(u"Ẩn tạm: {0} CAD / link / point cloud, {1} category".format(
            r["hideElements"], r["hideCategories"]))
    lines += [u"Bỏ qua: " + s for s in r["skipped"]] + r.get("failed", [])
    if r["onSheets"]:
        lines.append(u"View nằm trên sheet {0}: bấm lại để tắt trước khi in.".format(
            u", ".join(r["onSheets"])))
    say(u"Chế độ nhẹ: bật", lines)
