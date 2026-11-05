# -*- coding: utf-8 -*-
"""Sheets to DWG without bringing Revit down: one file per sheet, like the PDF export.

Same run as pdf_export (journal, crash memory, narrowing down), through DwgOut. A DWG
of a sheet holds its views side by side in model space and the title block + viewports
in paper space. A sheet goes out as one pass with every view blanked (paper space kept)
+ one pass per view (only its model space is used). Revit places a view in model space
according to the views before it and sizes a blanked view's viewport by what is left, so
each part is read first (where its viewport is, how much it covers), then with AutoCAD's
accoreconsole (no AutoCAD window, needed on the PC):
  - the viewports of the first file get the box and the model position of their view
    (viewports cannot be edited in place: through a DXF of that file, nearly empty);
  - what a blanked view still draws (view references survive every hidden category) is
    erased: it comes with that view's own pass;
  - names that mean something else in another part get a new name in that part: Revit
    numbers text styles, dimension styles, line types per file ("Arial Narrow_1" is 2 mm
    in one file, 500 in the next) and an INSERT would keep the first file's one;
  - the model space of each part goes into it as one block, shifted to that position.
    Not exploded: AutoCAD redraws a dimension it moves from its style, and Revit's
    dimension styles do not give back what Revit drew (text 100 times too small).
A part that should show something but came out empty counts as a failed pass, so a sheet
is never written with a view missing.

Images: Revit writes them next to the DWG by absolute path. They are copied to the
export folder and linked relatively, so the folder can be sent as it is.
"""
import os
import re
import shutil

from Autodesk.Revit.DB import (BuiltInParameter, DWGExportOptions, ExportDWGSettings,
                               FilteredElementCollector)
from System.Collections.Generic import List
from System.Diagnostics import Process, ProcessStartInfo
from System.IO import Directory

import pdf_export as pe

TIMEOUT_MS = 20 * 60 * 1000     # one console run
IMAGES = (u".jpg", u".jpeg", u".png", u".bmp", u".tif", u".tiff")


def accore():
    """accoreconsole.exe of the newest AutoCAD, or None (ACCORECONSOLE overrides)."""
    p = os.environ.get("ACCORECONSOLE")
    if p and os.path.exists(p):
        return p
    root = os.path.join(os.environ.get("ProgramFiles", u"C:\\Program Files"), u"Autodesk")
    if not os.path.isdir(root):
        return None
    found = [d for d in Directory.GetDirectories(root, "AutoCAD *")
             if os.path.exists(os.path.join(d, u"accoreconsole.exe"))]
    return os.path.join(sorted(found)[-1], u"accoreconsole.exe") if found else None


# ---- export setups ("template") of the model -----------------------------------------

def setups(doc):
    """Names of the model's DWG export setups, in Revit's order."""
    return list(ExportDWGSettings.ListNames(doc))


def default_setup(doc):
    """The setup used last if the model has it, else the active one, else the first."""
    names = setups(doc)
    last = pe.settings().get("dwg_setup")
    if last in names:
        return last
    active = ExportDWGSettings.GetActivePredefinedSettings(doc)
    if active is not None and active.Name in names:
        return active.Name
    return names[0] if names else None


def remember_setup(name):
    s = pe.settings()
    s["dwg_setup"] = name
    pe.save_settings(s)


def options(doc, name):
    """Options of the setup (no setup in the model: Revit's defaults), one file per sheet."""
    o = None
    if name:
        st = ExportDWGSettings.FindByName(doc, name)
        if st is None:
            raise Exception(u"model không có template xuất DWG \"{0}\"".format(name))
        o = st.GetDWGExportOptions()
    o = o or DWGExportOptions()
    o.MergedViews = True
    return o


# ---- where things go: plain numbers, no Revit, no AutoCAD -------------------------------

MARGIN = 0.25       # free model space around a view, as a part of its width


def _rect(v):
    return (v["cx"] - v["w"] / 2.0, v["cy"] - v["h"] / 2.0,
            v["cx"] + v["w"] / 2.0, v["cy"] + v["h"] / 2.0)


def _join_rect(a, b):
    return (min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3]))


def kept_viewports(vps, n=1):
    """The n viewports of a pass (n views drawn) over the most of its content: a blanked
    view shows nothing of its own, but its box can lie over the content."""
    return [v for v in sorted(vps, key=lambda v: -v["count"])[:n] if v["count"]]


def layout(base, parts, drawn=None):
    """base = viewports of the first file; parts = [viewports of each part], as read by
    _ANALYZE: {handle, cx, cy, w, h (paper), mx, my, mh (view centre, height in model),
    count}; drawn = views drawn in each part (default 1). Model = (paper - c) * mh / h + m,
    for each viewport of each pass.

    Returns (shift of each part, {base handle: final viewport {cx, cy, w, h, mx, my, mh}}):
    the views side by side, a view in crop tiles on one frame."""
    frames = {}        # base handle -> [x0, y0, scale, paper box]
    shifts = []
    cursor = 0.0
    near = lambda v: min(base, key=lambda b: (b["cx"] - v["cx"]) ** 2 + (b["cy"] - v["cy"]) ** 2)
    for k, vps in enumerate(parts):
        kept = [(v, near(v)) for v in kept_viewports(vps, drawn[k] if drawn else 1)]
        if not kept:
            shifts.append((0.0, 0.0))
            continue
        known = [(v, b) for v, b in kept if b["handle"] in frames]
        if known:
            v, b = known[0]
            f = frames[b["handle"]]
            s = v["mh"] / v["h"]
            dx, dy = f[0] - (v["mx"] - v["cx"] * s), f[1] - (v["my"] - v["cy"] * s)
        else:
            lo = min((v["mx"] - v["cx"] * (v["mh"] / v["h"])) + min(_rect(v)[0], _rect(b)[0]) * (v["mh"] / v["h"])
                     for v, b in kept)
            dx, dy = cursor - lo, 0.0
        right = cursor
        for v, b in kept:
            s = v["mh"] / v["h"]
            box = _rect(v)
            if b["handle"] in frames:
                frames[b["handle"]][3] = _join_rect(frames[b["handle"]][3], box)
                continue
            x0, y0 = v["mx"] + dx - v["cx"] * s, v["my"] + dy - v["cy"] * s
            frames[b["handle"]] = [x0, y0, s, box]
            wide = _join_rect(box, _rect(b))
            right = max(right, x0 + wide[2] * s + (wide[2] - wide[0]) * s * MARGIN)
        cursor = max(cursor, right)
        shifts.append((dx, dy))
    final = {}
    for b in base:
        if b["handle"] in frames:
            x0, y0, s, box = frames[b["handle"]]
            cx, cy = (box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0
            w, h = box[2] - box[0], box[3] - box[1]
            final[b["handle"]] = {"cx": cx, "cy": cy, "w": w, "h": h, "mx": x0 + cx * s,
                                  "my": y0 + cy * s, "mh": h * s}
        else:                   # nothing to show: its box must not look at another view
            mw = b["w"] * b["mh"] / b["h"]
            final[b["handle"]] = {"cx": b["cx"], "cy": b["cy"], "w": b["w"], "h": b["h"],
                                  "mx": cursor + mw / 2.0, "my": b["my"], "mh": b["mh"]}
            cursor += mw * (1 + MARGIN)
    return shifts, final


def dxf_viewports(text, final):
    """text of a DXF with the viewports in `final` (by handle) set; line ends kept."""
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    codes = (("10", "cx"), ("20", "cy"), ("40", "w"), ("41", "h"),
             ("12", "mx"), ("22", "my"), ("45", "mh"))
    done = 0
    i = 0
    while i + 1 < len(lines):
        if lines[i].strip() == "0" and lines[i + 1].strip() == "VIEWPORT":
            j, v, seen = i + 2, None, set()
            while j + 1 < len(lines) and lines[j].strip() != "0":
                code = lines[j].strip()
                if code == "5":
                    v = final.get(lines[j + 1].strip())
                elif v is not None and code not in seen:
                    for c, key in codes:
                        if c == code:
                            lines[j + 1] = "%.6f" % v[key]
                            seen.add(code)
                j += 2
            done += 1 if v is not None else 0
            i = j
        else:
            i += 2
    if done != len(final):
        raise Exception(u"DXF: sửa được {0}/{1} viewport".format(done, len(final)))
    return nl.join(lines)


# ---- accoreconsole ----------------------------------------------------------------------
# The scripts are pure ASCII and use bare file names of the temp folder, so paths with any
# characters are safe. The scripts report to text files, never through the console.

_ANALYZE = u"""(setvar "CMDECHO" 0)
(setq dxa-f (open (strcat (getvar "DWGPREFIX") "{0}.vp") "w"))
(setq dxa-vs (ssget "X" (list (cons 0 "VIEWPORT"))) dxa-ms (ssget "X" (list (cons 410 "Model"))) dxa-l nil dxa-i 0)
(defun dxa-n (v) (rtos v 2 6))
(if dxa-vs (while (< dxa-i (sslength dxa-vs)) (setq dxa-d (entget (ssname dxa-vs dxa-i))) (if (/= 1 (cdr (assoc 69 dxa-d))) (progn (setq dxa-w (* (cdr (assoc 40 dxa-d)) (/ (cdr (assoc 45 dxa-d)) (cdr (assoc 41 dxa-d)))) dxa-c (cdr (assoc 12 dxa-d))) (setq dxa-l (cons (list dxa-d (- (car dxa-c) (/ dxa-w 2)) (- (cadr dxa-c) (/ (cdr (assoc 45 dxa-d)) 2)) (+ (car dxa-c) (/ dxa-w 2)) (+ (cadr dxa-c) (/ (cdr (assoc 45 dxa-d)) 2)) 0) dxa-l)))) (setq dxa-i (1+ dxa-i))))
(setq dxa-t (mapcar (quote (lambda (x) 0)) dxa-l) dxa-i 0)
(if dxa-ms (while (< dxa-i (sslength dxa-ms)) (setq dxa-p (cdr (assoc 10 (entget (ssname dxa-ms dxa-i))))) (if dxa-p (setq dxa-t (mapcar (quote (lambda (r n) (if (and (>= (car dxa-p) (nth 1 r)) (<= (car dxa-p) (nth 3 r)) (>= (cadr dxa-p) (nth 2 r)) (<= (cadr dxa-p) (nth 4 r))) (1+ n) n))) dxa-l dxa-t))) (setq dxa-i (1+ dxa-i))))
(mapcar (quote (lambda (r n / d c p) (setq d (car r) c (cdr (assoc 12 d)) p (cdr (assoc 10 d))) (write-line (strcat "VP " (cdr (assoc 5 d)) " " (dxa-n (car p)) " " (dxa-n (cadr p)) " " (dxa-n (cdr (assoc 40 d))) " " (dxa-n (cdr (assoc 41 d))) " " (dxa-n (car c)) " " (dxa-n (cadr c)) " " (dxa-n (cdr (assoc 45 d))) " " (dxa-n (cdr (assoc 51 d))) " " (itoa n)) dxa-f))) dxa-l dxa-t)
(setq dxa-b (getvar "INSBASE"))
(write-line (strcat "INSBASE " (dxa-n (car dxa-b)) " " (dxa-n (cadr dxa-b))) dxa-f)
(defun dxa-s (v) (cond ((= (type v) (quote STR)) v) ((= (type v) (quote INT)) (itoa v)) ((= (type v) (quote REAL)) (rtos v 2 6)) ((= (type v) (quote ENAME)) (dxa-e v)) (T "")))
(defun dxa-e (e / d) (setq d (entget e)) (if (= "STYLE" (cdr (assoc 0 d))) (dxa-style d) (if (assoc 2 d) (cdr (assoc 2 d)) "")))
(defun dxa-style (d) (strcat (cdr (assoc 3 d)) "|" (dxa-s (cdr (assoc 40 d))) "|" (dxa-s (cdr (assoc 41 d))) "|" (dxa-s (cdr (assoc 50 d))) "|" (dxa-s (cdr (assoc 70 d)))))
(defun dxa-row (r / o) (setq o "") (foreach x r (if (and (listp x) (/= 2 (car x))) (setq o (strcat o (itoa (car x)) "=" (dxa-s (cdr x)) ";")))) o)
(defun dxa-blk (b / e n) (setq e (cdr (assoc -2 b)) n 0) (while e (setq n (1+ n) e (entnext e))) (itoa n))
(defun dxa-out (k n g) (write-line (strcat "T" (chr 9) k (chr 9) n (chr 9) g) dxa-f))
(setq dxa-r (tblnext "STYLE" T)) (while dxa-r (if (/= "" (cdr (assoc 2 dxa-r))) (dxa-out "Style" (cdr (assoc 2 dxa-r)) (dxa-style dxa-r))) (setq dxa-r (tblnext "STYLE")))
(setq dxa-r (tblnext "DIMSTYLE" T)) (while dxa-r (dxa-out "Dimstyle" (cdr (assoc 2 dxa-r)) (dxa-row dxa-r)) (setq dxa-r (tblnext "DIMSTYLE")))
(setq dxa-r (tblnext "LTYPE" T)) (while dxa-r (dxa-out "LType" (cdr (assoc 2 dxa-r)) (dxa-row dxa-r)) (setq dxa-r (tblnext "LTYPE")))
(setq dxa-r (tblnext "LAYER" T)) (while dxa-r (dxa-out "LAyer" (cdr (assoc 2 dxa-r)) (strcat (itoa (abs (cdr (assoc 62 dxa-r)))) "|" (cdr (assoc 6 dxa-r)) "|" (dxa-s (cdr (assoc 370 (entget (tblobjname "LAYER" (cdr (assoc 2 dxa-r))))))))) (setq dxa-r (tblnext "LAYER")))
(setq dxa-r (tblnext "BLOCK" T)) (while dxa-r (if (and (= 0 (logand 5 (cdr (assoc 70 dxa-r)))) (/= "*" (substr (cdr (assoc 2 dxa-r)) 1 1))) (dxa-out "Block" (cdr (assoc 2 dxa-r)) (dxa-blk dxa-r))) (setq dxa-r (tblnext "BLOCK")))
(close dxa-f)
"""
_DXF_OUT = u"""(setvar "FILEDIA" 0)
(setvar "CMDECHO" 0)
(command "_.DXFOUT" "base.dxf" "16")
"""
_DXF_IN = u"""(setvar "FILEDIA" 0)
(setvar "CMDECHO" 0)
(setvar "ISAVEBAK" 0)
(command "_.SAVEAS" "{0}" "base2.dwg")
"""
_MERGE = u"""(setvar "FILEDIA" 0)
(setvar "CMDECHO" 0)
(setvar "ISAVEBAK" 0)
(setq dxr-log (open (strcat (getvar "DWGPREFIX") "merge.log") "w"))
(defun dxr-count (space / ss) (setq ss (ssget "X" (list (cons 410 space)))) (if ss (sslength ss) 0))
(defun dxr-all (/ ss) (setq ss (ssget "X")) (if ss (sslength ss) 0))
(defun dxr-tail (p / i) (setq i (strlen p)) (while (and (> i 0) (not (member (substr p i 1) (list "\\\\" "/")))) (setq i (1- i))) (substr p (1+ i)))
(defun dxr-images (rel / dict rec e d) (setq dict (dictsearch (namedobjdict) "ACAD_IMAGE_DICT")) (if dict (foreach rec dict (if (= 350 (car rec)) (progn (setq d (entget (cdr rec))) (if (assoc 1 d) (progn (write-line (strcat "IMAGE " (dxr-tail (cdr (assoc 1 d)))) dxr-log) (entmod (subst (cons 1 (strcat rel (dxr-tail (cdr (assoc 1 d))))) (assoc 1 d) d)))))))))
(setq dxr-tab (getvar "CTAB"))
(setq dxr-m (ssget "X" (list (cons 410 "Model"))) dxr-i 0)
(if dxr-m (while (< dxr-i (sslength dxr-m)) (entdel (ssname dxr-m dxr-i)) (setq dxr-i (1+ dxr-i))))
(write-line (strcat "BASE " (itoa (dxr-all))) dxr-log)
(setvar "TILEMODE" 1)
"""
_INSERT = u"""(progn (setq dxr-n (dxr-count "Model")) (command "_.-INSERT" "{0}" "{1:.6f},{2:.6f},0" "1" "1" "0") (write-line (strcat "PART {0} " (itoa (- (dxr-count "Model") dxr-n))) dxr-log) (princ))
"""
_END = u"""(setvar "CTAB" dxr-tab)
(dxr-images "{0}")
(write-line (strcat "TOTAL " (itoa (dxr-all))) dxr-log)
(close dxr-log)
(command "_.QSAVE")
"""
# block names of a tile are the view's: its parts would fall back on the first tile's blocks
_RENAME_HEAD = u"""(setvar "FILEDIA" 0)
(setvar "CMDECHO" 0)
(setvar "ISAVEBAK" 0)
"""
# texts in the box of a blanked view (l b r t) and out of the kept ones
_STRAY = u"""(defun dxr-in (p r) (and (>= (car p) (nth 0 r)) (<= (car p) (nth 2 r)) (>= (cadr p) (nth 1 r)) (<= (cadr p) (nth 3 r))))
(defun dxr-any (p l / a) (setq a nil) (foreach r l (if (dxr-in p r) (setq a T))) a)
(setq dxr-off (list {0}) dxr-on (list {1}) dxr-s (ssget "X" (list (cons 410 "Model") (cons 0 "MTEXT,TEXT"))) dxr-i 0)
(if dxr-s (while (< dxr-i (sslength dxr-s)) (setq dxr-e (ssname dxr-s dxr-i) dxr-p (cdr (assoc 10 (entget dxr-e)))) (if (and (dxr-any dxr-p dxr-off) (not (dxr-any dxr-p dxr-on))) (entdel dxr-e)) (setq dxr-i (1+ dxr-i))))
"""
_RENAME = _RENAME_HEAD + u"""(setq dxr-names nil dxr-b (tblnext "BLOCK" T))
(while dxr-b (if (and (= 0 (logand 5 (cdr (assoc 70 dxr-b)))) (/= "*" (substr (cdr (assoc 2 dxr-b)) 1 1))) (setq dxr-names (cons (cdr (assoc 2 dxr-b)) dxr-names))) (setq dxr-b (tblnext "BLOCK")))
(foreach dxr-x dxr-names (command "_.-RENAME" "_Block" dxr-x (strcat dxr-x "{0}")))
(command "_.QSAVE")
"""


def _console(exe, folder, name, script):
    """Run the script on folder\\name (a .dwg or .dxf) in AutoCAD's console. Names from a
    DWG come and go as the same bytes (latin-1 only carries them)."""
    scr = os.path.join(folder, u"run.scr")
    with open(scr, "wb") as f:
        f.write(script.encode("latin-1"))
    psi = ProcessStartInfo(exe, u'/i "{0}" /s "{1}" /l en-US'.format(os.path.join(folder, name), scr))
    psi.UseShellExecute = False
    psi.CreateNoWindow = True
    psi.WorkingDirectory = folder
    p = Process.Start(psi)
    if not p.WaitForExit(TIMEOUT_MS):
        p.Kill()
        raise Exception(u"AutoCAD console quá thời gian khi ghép DWG")


# names that are the same thing for AutoCAD in every file
_SHARED = {"LAyer": (u"0", u"Defpoints"), "LType": (u"ByLayer", u"ByBlock", u"Continuous")}


def unique_names(tables, part):
    """tables = {kind: {name: [(what it means, name in the result)]}}, part = [(kind, name,
    what it means)] of the next file. Returns [(kind, name, new name)] to rename in it first."""
    res = []
    for kind, name, sig in part:
        if name in _SHARED.get(kind, ()):
            continue
        known = tables.setdefault(kind, {}).setdefault(name, [])
        same = [final for g, final in known if g == sig]
        if same:
            if same[0] != name:
                res.append((kind, name, same[0]))
            continue
        final = name if not known else u"{0}_{1}".format(name, len(known) + 1)
        known.append((sig, final))
        if final != name:
            res.append((kind, name, final))
    return res


def _lisp(text):
    return text.replace(u"\\", u"\\\\").replace(u'"', u'\\"')


def _model_box(v):
    mw = v["w"] * v["mh"] / v["h"]
    return u"(list {0:.6f} {1:.6f} {2:.6f} {3:.6f})".format(
        v["mx"] - mw / 2.0, v["my"] - v["mh"] / 2.0, v["mx"] + mw / 2.0, v["my"] + v["mh"] / 2.0)


def stray_script(vps, drawn):
    """Script that erases what the blanked viewports of a part hold, or u"" if nothing."""
    kept = kept_viewports(vps, drawn)
    off = [v for v in vps if v["count"] and v not in kept]
    if not off:
        return u""
    return _STRAY.format(u" ".join(_model_box(v) for v in off), u" ".join(_model_box(v) for v in kept))


def _read_viewports(exe, folder, name):
    """(viewports, INSBASE, [(kind, name, what it means)]) of a part."""
    _console(exe, folder, name + u".dwg", _ANALYZE.format(name))
    out, ins, names = [], (0.0, 0.0), []
    path = os.path.join(folder, name + u".vp")
    if not os.path.exists(path):
        raise Exception(u"AutoCAD console không đọc được {0}".format(name))
    with open(path, "rb") as f:
        for line in f.read().decode("latin-1").splitlines():
            if line.startswith(u"T\t"):
                names.append(tuple(line.split(u"\t", 3)[1:]))
                continue
            w = line.split(u" ")
            if w[0] == u"INSBASE":
                ins = (float(w[1]), float(w[2]))
            if w[0] != u"VP":
                continue
            n = [float(x) for x in w[2:10]]
            if abs(n[7]) > 1e-6:
                raise Exception(u"viewport xoay trong DWG, chưa ghép được ({0})".format(name))
            out.append({"handle": w[1], "cx": n[0], "cy": n[1], "w": n[2], "h": n[3],
                        "mx": n[4], "my": n[5], "mh": n[6], "count": int(w[10])})
    return out, ins, names


def merge(exe, folder, base, parts, drawn, tiled, rel, version="2018"):
    """Put the parts (bare file names in folder, drawn = views in each) into base: viewports
    set, model spaces inserted, images linked as rel + name; result = folder\\base2.dwg.
    Returns {"base": entities, "parts": {name: added}, "viewports": {name: kept}, "images":
    [names], "total"}."""
    log = os.path.join(folder, u"merge.log")
    for k, name in enumerate(tiled):
        _console(exe, folder, name + u".dwg", _RENAME.format(u"__t{0}".format(k + 1)))
    base_vps, _, base_names = _read_viewports(exe, folder, base)
    read = [_read_viewports(exe, folder, n) for n in parts]
    part_vps = [r[0] for r in read]
    tables = {}
    unique_names(tables, base_names)
    for n, r, d in zip(parts, read, drawn):
        ren = unique_names(tables, r[2])
        stray = stray_script(r[0], d)
        if ren or stray:
            # through a free name first: a new name may be one this file still uses for another
            cmd = u'(command "_.-RENAME" "_{0}" "{1}" "{2}")\n'
            _console(exe, folder, n + u".dwg", _RENAME_HEAD + stray +
                     u"".join(cmd.format(k, _lisp(a), u"dxr-free-{0}".format(i)) for i, (k, a, b) in enumerate(ren)) +
                     u"".join(cmd.format(k, u"dxr-free-{0}".format(i), _lisp(b)) for i, (k, a, b) in enumerate(ren)) +
                     u'(command "_.QSAVE")\n')
    shifts, final = layout(base_vps, part_vps, drawn)
    # INSERT puts a file's base point (INSBASE, Revit sets it per pass) at the point given
    at = [(sh[0] + r[1][0], sh[1] + r[1][1]) for sh, r in zip(shifts, read)]
    _console(exe, folder, base + u".dwg", _DXF_OUT)
    dxf = os.path.join(folder, u"base.dxf")
    if not os.path.exists(dxf):
        raise Exception(u"AutoCAD console không xuất được DXF của {0}".format(base))
    with open(dxf, "rb") as f:
        text = f.read()
    text = dxf_viewports(text, final) if final else text
    with open(dxf, "wb") as f:
        f.write(text)
    _console(exe, folder, u"base.dxf", _DXF_IN.format(version))
    script = _MERGE + u"".join(_INSERT.format(n, s[0], s[1]) for n, s in zip(parts, at)) + \
        _END.format(rel.replace(u"\\", u"\\\\"))
    _console(exe, folder, u"base2.dwg", script)
    res = {"base": None, "parts": {}, "images": [], "total": None,
           "viewports": dict((n, len(kept_viewports(v, d))) for n, v, d in zip(parts, part_vps, drawn))}
    if os.path.exists(log):
        with open(log, "rb") as f:
            for line in f.read().decode("utf-8", "replace").splitlines():
                w = line.split(u" ", 2)
                if w[0] == u"BASE":
                    res["base"] = int(w[1])
                elif w[0] == u"PART":
                    res["parts"][w[1]] = int(w[2])
                elif w[0] == u"IMAGE":
                    res["images"].append(line[6:])
                elif w[0] == u"TOTAL":
                    res["total"] = int(w[1])
    return res


# ---- the format ------------------------------------------------------------------------

class DwgOut(pe.PdfOut):
    kind = "dwg"
    noun = u"DWG"
    always_split = True     # CAD export dies far more easily than PDF: always view by view
    overlay = False         # the parts are not whole pages: nothing hidden, nothing aligned

    def __init__(self, doc, setup=None):
        self.setup = setup if setup is not None else default_setup(doc)
        self.root = None        # set by the run: the export folder
        self.exe = accore()
        self.opts = None

    def check(self, doc):
        if not self.exe:
            return u"máy chưa có AutoCAD (accoreconsole.exe) để ghép các phần DWG"
        try:
            self.opts = options(doc, self.setup)
        except Exception as ex:
            return u"{0}".format(ex)
        return u""

    def key(self):
        return u"|dwg|{0}".format(self.setup or u"")

    def prepare(self, doc, sheet):
        return u"DWG"

    def file_name(self, sheet):
        """<number>-<revision>, as ProSheets names them."""
        rev = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)
        rev = rev.AsString() if rev is not None else u""
        name = u"{0}-{1}".format(sheet.SheetNumber, rev) if rev else sheet.SheetNumber
        return pe._BAD.sub(u"_", name).strip(u" .")

    def path(self, target, sheet):
        return os.path.join(target, self.file_name(sheet) + u".dwg")

    def export(self, doc, sheet, folder, name):
        path = os.path.join(folder, name + u".dwg")
        pe._remove(path)
        ok = doc.Export(folder, name, List[pe.ElementId]([sheet.Id]), self.opts)
        if not ok or not os.path.exists(path):
            raise pe.ExportFailed(u"Revit không xuất được {0}".format(name))
        return path

    def whole(self, doc, sheet, plan, target):
        if not os.path.isdir(pe.PARTS_DIR):
            os.makedirs(pe.PARTS_DIR)
        f = self.export(doc, sheet, pe.PARTS_DIR, self.file_name(sheet))
        return self.join(doc, sheet, plan, target, [(f, [])])

    def _shown(self, doc, view_ids):
        """True when one of these views has something to show in Revit."""
        for vid in view_ids:
            if FilteredElementCollector(doc, pe.ElementId(int(vid))) \
                    .WhereElementIsNotElementType().GetElementCount():
                return True
        return False

    def _version(self):
        """AutoCAD format of the setup, for SAVEAS (R2018 -> 2018)."""
        m = re.search(r"\d{4}", str(self.opts.FileVersion))
        return m.group(0) if m else "2018"

    def join(self, doc, sheet, plan, target, files):
        # short ASCII names in a folder of its own: the scripts only see those
        work = os.path.join(pe.PARTS_DIR, u"join")
        if os.path.isdir(work):
            Directory.Delete(work, True)
        os.makedirs(work)
        names, tiled = [], []
        tiles = _tile_flags(plan)
        for k, (f, _) in enumerate(files):
            n = u"p{0}".format(k + 1)
            shutil.move(f, os.path.join(work, n + u".dwg"))
            names.append(n)
            if tiles[k]:
                tiled.append(n)
        for f in Directory.GetFiles(pe.PARTS_DIR):
            if os.path.splitext(f)[1].lower() in IMAGES:
                shutil.move(f, os.path.join(work, os.path.basename(f)))
        depth = len(os.path.relpath(target, self.root).split(os.sep)) if self.root and \
            os.path.normcase(target) != os.path.normcase(self.root) else 0
        res = merge(self.exe, work, names[0], names[1:], [max(1, len(v)) for _, v in files[1:]], tiled,
                    u"..\\" * depth, self._version())
        if res["total"] is None:
            raise Exception(u"AutoCAD console không ghép được {0}".format(self.file_name(sheet)))
        if not res["base"]:
            self._empty(doc, sheet, plan, [], u"khung tên")
        for k, n in enumerate(names[1:]):
            if not tiles[k + 1] and self._shown(doc, files[k + 1][1]) and not res["viewports"].get(n):
                self._empty(doc, sheet, plan, files[k + 1][1], n)
        for img in res["images"]:
            src = os.path.join(work, img)
            if os.path.exists(src):
                shutil.copyfile(src, os.path.join(self.root or target, img))
        path = self.path(target, sheet)
        pe._remove(path)
        shutil.copyfile(os.path.join(work, u"base2.dwg"), path)
        Directory.Delete(work, True)
        return path

    def _empty(self, doc, sheet, plan, views, what):
        """A part came out empty: same as a pass Revit refused, so the sheet is narrowed."""
        pe.mark_pass(doc, sheet, plan, views, kind=self.kind)
        raise pe.ExportFailed(u"DWG xuất ra rỗng ({0})".format(what))


class _Passes(object):
    overlay = False


def _tile_flags(plan):
    """For each file of a plan, in pass order: is it a crop tile of a view?"""
    if not plan.get("parts"):
        return [False]
    return [kind == "tile" for _, kind, _ in pe._passes(plan, _Passes())]
