# -*- coding: utf-8 -*-
"""Small PDF joiner in pure Python (IronPython 2.7 / CPython), no outside tool.

Two jobs for the sheet export:
  - join: pages of many PDFs one after the other into one file, one bookmark per entry;
  - overlay: the part files of one sheet (same paper) drawn on top of each other
    into one page, each part as a Form XObject, so a heavy sheet exported in parts
    comes out as one page.

Reading is a forward scan of "n g obj ... endobj" (classic xref, incremental updates,
xref / object streams all read), so it does not rely on the xref offsets. Each source
is read, copied and dropped before the next, so the joined file never sits in memory.
"""
import re
import zlib

WS = b" \t\r\n\x0c\x00"
DELIM = b"()<>[]{}/%"
_OBJ = re.compile(br"(\d+)\s+(\d+)\s+obj\b")
_TRAILER = re.compile(br"trailer\s*<<")


class PdfError(Exception):
    pass


class Name(str):
    """/Name, kept as written (without the slash)."""


class Raw(str):
    """Token written back as is: number, string, keyword."""


class Ref(object):
    __slots__ = ("num", "gen")

    def __init__(self, num, gen=0):
        self.num, self.gen = num, gen


class Stream(object):
    __slots__ = ("dict", "data")

    def __init__(self, d, data):
        self.dict, self.data = d, data


# ---- reading ----------------------------------------------------------------------

def _skip(data, i):
    n = len(data)
    while i < n:
        c = data[i]
        if c in WS:
            i += 1
        elif c == "%":
            while i < n and data[i] not in "\r\n":
                i += 1
        else:
            break
    return i


def _token_end(data, i):
    n = len(data)
    while i < n and data[i] not in WS and data[i] not in DELIM:
        i += 1
    return i


def _literal_end(data, i):
    """i at '('; index just past the matching ')'."""
    depth, n = 0, len(data)
    while i < n:
        c = data[i]
        if c == "\\":
            i += 2
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    raise PdfError("unterminated string")


def parse(data, i):
    """(value, next index) of the PDF object at data[i:]."""
    i = _skip(data, i)
    c = data[i]
    if c == "/":
        j = _token_end(data, i + 1)
        return Name(data[i + 1:j]), j
    if c == "<":
        if data[i + 1] == "<":
            d, i = {}, i + 2
            while True:
                i = _skip(data, i)
                if data[i] == ">":
                    return d, i + 2
                k, i = parse(data, i)
                v, i = parse(data, i)
                d[str(k)] = v
        j = data.index(">", i)
        return Raw(data[i:j + 1]), j + 1
    if c == "[":
        a, i = [], i + 1
        while True:
            i = _skip(data, i)
            if data[i] == "]":
                return a, i + 1
            v, i = parse(data, i)
            if v == "R" and isinstance(v, Raw) and len(a) >= 2:
                g, n = a.pop(), a.pop()
                v = Ref(int(n), int(g))
            a.append(v)
    if c == "(":
        j = _literal_end(data, i)
        return Raw(data[i:j]), j
    j = _token_end(data, i)
    if j == i:
        raise PdfError("bad token at %d" % i)
    tok = data[i:j]
    if tok[0] in "+-.0123456789":
        # "n g R" outside an array (dict values)
        k = _skip(data, j)
        m = re.match(r"(\d+)\s+R\b", data[k:k + 24]) if tok.isdigit() else None
        if m:
            return Ref(int(tok), int(m.group(1))), k + m.end()
    return Raw(tok), j


def _decode(st):
    f = st.dict.get("Filter")
    if f is None:
        return st.data
    filters = f if isinstance(f, list) else [f]
    if [str(x) for x in filters] != ["FlateDecode"]:
        raise PdfError("filter %s not supported" % filters)
    parms = st.dict.get("DecodeParms")
    if isinstance(parms, list):
        parms = parms[0] if parms else None
    out = zlib.decompress(st.data)
    pred = int(parms.get("Predictor", 1)) if isinstance(parms, dict) else 1
    if pred >= 10:
        out = _png_unpredict(out, int(parms.get("Columns", 1)))
    return out


def _png_unpredict(data, cols):
    """PNG Up / Sub / None rows (xref streams); enough for what writers use."""
    row = cols + 1
    prev = [0] * cols
    out = []
    for r in range(0, len(data), row):
        t = ord(data[r])
        cur = [ord(ch) for ch in data[r + 1:r + row]]
        if t == 2:
            cur = [(a + b) & 255 for a, b in zip(cur, prev)]
        elif t == 1:
            for k in range(1, len(cur)):
                cur[k] = (cur[k] + cur[k - 1]) & 255
        elif t != 0:
            raise PdfError("png predictor %d not supported" % t)
        out.append("".join(chr(x) for x in cur))
        prev = cur
    return "".join(out)


class Reader(object):
    def __init__(self, path):
        with open(path, "rb") as f:
            data = f.read()
        if not data.startswith("%PDF"):
            raise PdfError("not a PDF: %s" % path)
        self.objects = {}
        self.trailer = {}
        self._scan(data)
        if "Root" not in self.trailer:
            raise PdfError("no /Root in %s" % path)

    def _scan(self, data):
        i, n = 0, len(data)
        objstms = []
        while True:
            m = _OBJ.search(data, i)
            t = _TRAILER.search(data, i)
            if t and (not m or t.start() < m.start()):
                d, i = parse(data, t.end() - 2)
                self.trailer.update(d)
                continue
            if not m:
                break
            num = int(m.group(1))
            try:
                v, j = parse(data, m.end())
            except Exception:
                i = m.end()
                continue
            k = _skip(data, j)
            if data.startswith("stream", k) and isinstance(v, dict):
                k += 6
                if data.startswith("\r\n", k):
                    k += 2
                elif data[k] in "\r\n":
                    k += 1
                ln = v.get("Length")
                end = -1
                if isinstance(ln, Raw) and ln.isdigit():
                    e = k + int(ln)
                    if data.startswith("endstream", _skip(data, e)):
                        end = e
                if end < 0:
                    end = data.index("endstream", k)
                    while end > k and data[end - 1] in "\r\n":
                        end -= 1
                v = Stream(v, data[k:end])
                j = data.index("endstream", end) + 9
                typ = v.dict.get("Type")
                if typ == "XRef":
                    for key in ("Root", "Info"):
                        if key in v.dict:
                            self.trailer[key] = v.dict[key]
                elif typ == "ObjStm":
                    objstms.append(v)
            self.objects[num] = v
            i = j
        for st in objstms:
            body = _decode(st)
            first, count = int(st.dict["First"]), int(st.dict["N"])
            nums = body[:first].split()
            for x in range(count):
                num, off = int(nums[2 * x]), int(nums[2 * x + 1])
                if num not in self.objects:
                    self.objects[num] = parse(body, first + off)[0]

    def get(self, v):
        """Follow a Ref (also chains of them)."""
        seen = 0
        while isinstance(v, Ref):
            v = self.objects.get(v.num)
            seen += 1
            if seen > 50:
                raise PdfError("reference loop")
        return v

    def pages(self):
        """Page dicts in order, inherited Resources / MediaBox / CropBox / Rotate filled in."""
        out = []
        root = self.get(self.trailer["Root"])

        def walk(node, inh, depth):
            node = self.get(node)
            if depth > 64 or not isinstance(node, dict):
                return
            inh = dict(inh)
            for key in ("Resources", "MediaBox", "CropBox", "Rotate"):
                if key in node:
                    inh[key] = node[key]
            if node.get("Type") == "Pages" or "Kids" in node:
                for kid in self.get(node.get("Kids")) or []:
                    walk(kid, inh, depth + 1)
            else:
                page = dict(node)
                for key, val in inh.items():
                    page.setdefault(key, val)
                out.append(page)

        walk(root["Pages"], {}, 0)
        return out

    def content(self, page):
        """Page contents as one decoded byte string."""
        c = self.get(page.get("Contents"))
        parts = c if isinstance(c, list) else [c]
        out = []
        for p in parts:
            st = self.get(p)
            if isinstance(st, Stream):
                out.append(_decode(st))
        return "\n".join(out)


# ---- writing ----------------------------------------------------------------------

def _ser(v):
    if isinstance(v, Name):
        return "/" + v
    if isinstance(v, Raw):
        return str(v)
    if isinstance(v, Ref):
        return "%d %d R" % (v.num, v.gen)
    if isinstance(v, dict):
        return "<<" + "".join("/%s %s" % (k, _ser(x)) for k, x in v.items()) + ">>"
    if isinstance(v, list):
        return "[" + " ".join(_ser(x) for x in v) + "]"
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, long)):     # noqa: F821 (IronPython 2)
        return str(v)
    if isinstance(v, float):
        return ("%.4f" % v).rstrip("0").rstrip(".")
    if v is None:
        return "null"
    return str(v)


def text_string(s):
    """Unicode title as a PDF text string (UTF-16BE hex with BOM)."""
    return Raw("<FEFF" + u"".join(u"%04X" % ord(ch) for ch in s) + ">")


class Writer(object):
    def __init__(self, path):
        self.f = open(path, "wb")
        self.f.write("%PDF-1.6\n%\xe2\xe3\xcf\xd3\n")
        self.pos = len("%PDF-1.6\n%\xe2\xe3\xcf\xd3\n")
        self.offsets = {}
        self.next = 1
        self.pages_ref = self.reserve()
        self.kids = []
        self.marks = []      # (title, page ref)

    def reserve(self):
        r = Ref(self.next)
        self.next += 1
        return r

    def put(self, v, ref=None):
        ref = ref or self.reserve()
        self.offsets[ref.num] = self.pos
        if isinstance(v, Stream):
            d = dict(v.dict)
            d["Length"] = len(v.data)
            for key in ("DL",):
                d.pop(key, None)
            s = "%d 0 obj\n%s\nstream\n" % (ref.num, _ser(d))
            self._w(s)
            self._w(v.data)
            self._w("\nendstream\nendobj\n")
        else:
            self._w("%d 0 obj\n%s\nendobj\n" % (ref.num, _ser(v)))
        return ref

    def _w(self, s):
        self.f.write(s)
        self.pos += len(s)

    # copy objects of one source, refs renumbered
    def copier(self, rd):
        memo = {}

        def cp(v):
            if isinstance(v, Ref):
                if v.num in memo:
                    return memo[v.num]
                obj = rd.objects.get(v.num)
                if obj is None:
                    return None
                ref = self.reserve()
                memo[v.num] = ref
                self.put(cp_val(obj), ref)
                return ref
            return cp_val(v)

        def cp_val(v):
            if isinstance(v, Stream):
                return Stream(cp_val(v.dict), v.data)
            if isinstance(v, dict):
                return dict((k, cp(x)) for k, x in v.items())
            if isinstance(v, list):
                return [cp(x) for x in v]
            return v
        return cp

    def add_page(self, rd, page, cp, overlays=(), underlays=()):
        """Copy page of reader rd; overlays = [(reader, page)] drawn on top of it,
        underlays drawn before it (opaque raster parts)."""
        p = {}
        for k, v in page.items():
            if k in ("Parent", "Annots", "StructParents", "B", "Thumb", "PieceInfo", "Tabs"):
                continue
            p[k] = cp(v)
        p["Type"] = Name("Page")
        p["Parent"] = self.pages_ref
        if overlays or underlays:
            res = rd.get(page.get("Resources")) or {}
            cp_res = dict((k, cp(v)) for k, v in res.items())
            xobj = dict((k, cp(v)) for k, v in (rd.get(res.get("XObject")) or {}).items())
            draw, under = [], []
            for n, (ord_, opage) in enumerate(list(underlays) + list(overlays)):
                form = self._form(ord_, opage)
                key = "Part%d" % (n + 1)
                while key in xobj:
                    key += "x"
                xobj[key] = form
                (under if n < len(underlays) else draw).append("q /%s Do Q" % key)
            cp_res["XObject"] = xobj
            p["Resources"] = cp_res
            contents = page.get("Contents")
            arr = rd.get(contents)
            arr = list(arr) if isinstance(arr, list) else [contents]
            head = self.put(Stream({}, "\n".join(under) + "\nq\n"))
            tail = self.put(Stream({}, "\nQ\n" + "\n".join(draw) + "\n"))
            p["Contents"] = [head] + [cp(x) for x in arr] + [tail]
        ref = self.put(p)
        self.kids.append(ref)
        return ref

    def _form(self, rd, page):
        cp = self.copier(rd)
        data = zlib.compress(rd.content(page), 6)
        d = {"Type": Name("XObject"), "Subtype": Name("Form"),
             "BBox": cp(page.get("MediaBox")), "Resources": cp(page.get("Resources") or {}),
             "Filter": Name("FlateDecode")}
        return self.put(Stream(d, data))

    def close(self, title=None):
        self.put({"Type": Name("Pages"), "Kids": self.kids, "Count": len(self.kids)},
                 self.pages_ref)
        root = {"Type": Name("Catalog"), "Pages": self.pages_ref}
        if self.marks:
            root["Outlines"] = self._outlines()
            root["PageMode"] = Name("UseOutlines")
        root_ref = self.put(root)
        info = {"Producer": Raw("(Revit sheet export)")}
        if title:
            info["Title"] = text_string(title)
        info_ref = self.put(info)
        xref = self.pos
        out = ["xref\n0 %d\n" % self.next, "0000000000 65535 f \n"]
        for num in range(1, self.next):
            off = self.offsets.get(num)
            out.append("%010d 00000 n \n" % off if off is not None else "0000000000 65535 f \n")
        out.append("trailer\n<</Size %d/Root %d 0 R/Info %d 0 R>>\nstartxref\n%d\n%%%%EOF\n"
                   % (self.next, root_ref.num, info_ref.num, xref))
        self._w("".join(out))
        self.f.close()

    def _outlines(self):
        top = self.reserve()
        refs = [self.reserve() for _ in self.marks]
        for k, ((title, page), ref) in enumerate(zip(self.marks, refs)):
            item = {"Title": text_string(title), "Parent": top,
                    "Dest": [page, Name("Fit")]}
            if k:
                item["Prev"] = refs[k - 1]
            if k + 1 < len(refs):
                item["Next"] = refs[k + 1]
            self.put(item, ref)
        self.put({"Type": Name("Outlines"), "First": refs[0], "Last": refs[-1],
                  "Count": len(refs)}, top)
        return top


# ---- the two jobs ---------------------------------------------------------------

def join(entries, out_path, title=None):
    """entries = [(bookmark title, base pdf, [part pdfs], optional [under pdfs])]: every
    page of each base, the i-th page of each part drawn over the i-th base page (under
    it for the under pdfs). Returns the page count."""
    w = Writer(out_path)
    try:
        for entry in entries:
            mark, base, parts = entry[:3]
            unders = entry[3] if len(entry) > 3 else []
            rd = Reader(base)
            prs = [Reader(p) for p in list(parts) + list(unders)]
            ppages = [r.pages() for r in prs]
            cp = w.copier(rd)
            for i, page in enumerate(rd.pages()):
                ov = [(r, pp[i]) for r, pp in zip(prs, ppages) if i < len(pp)]
                for r, op in ov:
                    if _box(r, op) != _box(rd, page):
                        raise PdfError("part paper differs from base: %s" % mark)
                n_over = len([1 for pp in ppages[:len(parts)] if i < len(pp)])
                ref = w.add_page(rd, page, cp, ov[:n_over], ov[n_over:])
                if i == 0 and mark:
                    w.marks.append((mark, ref))
            del rd, prs, ppages
    except Exception:
        w.f.close()
        raise
    w.close(title)
    return len(w.kids)


def _box(rd, page):
    return [round(float(rd.get(x)), 1) for x in rd.get(page.get("MediaBox"))]


def page_count(path):
    return len(Reader(path).pages())
