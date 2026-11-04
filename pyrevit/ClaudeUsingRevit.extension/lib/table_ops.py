# -*- coding: utf-8 -*-
"""Plain helpers of the table panel (lib/pane.py), no Revit, no WPF: values, number series, TSV / CSV text,
.xlsx files (System.IO.Compression + System.Xml, no library)."""
import io
import re

import clr
clr.AddReference('System.IO.Compression')
clr.AddReference('System.IO.Compression.FileSystem')
clr.AddReference('System.Xml')

from System.IO import FileAccess, FileMode, FileStream, StreamReader, StreamWriter
from System.IO.Compression import ZipArchive, ZipArchiveMode
from System.Text import UTF8Encoding
from System.Xml import XmlDocument

YES = (u'yes', u'y', u'1', u'true', u'có', u'co', u'x', u'✓')
NO = (u'no', u'n', u'0', u'false', u'không', u'khong', u'')


def normalize(kind, value):
    """The text a cell of this kind holds, or None when it cannot hold this value."""
    v = u'' if value is None else u'{0}'.format(value)
    if kind == 'yesno':
        s = v.strip().lower()
        return u'Yes' if s in YES else u'No' if s in NO else None
    if kind == 'int':
        s = v.strip()
        return s if re.match(r'^-?\d*$', s) else None
    if kind == 'number':
        return v.strip()
    return v.replace(u'\r\n', u' ').replace(u'\n', u' ')


def natural_key(text):
    """Sort text so that A-9 comes before A-10."""
    return re.sub(r'\d+', lambda m: m.group(0).zfill(14), (text or u'').lower())


def series_values(seeds, count):
    """The next count values after seeds: trailing number + step keeping its zeros, else the seeds repeated."""
    found = [re.match(r'^(.*?)(\d+)(\D*)$', s or u'', re.S) for s in seeds]
    if not all(found) or any(m.group(1) != found[0].group(1) or m.group(3) != found[0].group(3) for m in found):
        return [seeds[i % len(seeds)] for i in range(count)]
    last = found[-1]
    width, pad = len(last.group(2)), last.group(2).startswith(u'0')
    step = int(last.group(2)) - int(found[-2].group(2)) if len(found) > 1 else 1
    out = []
    for i in range(count):
        n = int(last.group(2)) + step * (i + 1)
        t = u'{0}'.format(abs(n))
        out.append(last.group(1) + (u'-' if n < 0 else u'') + (t.zfill(width) if pad else t) + last.group(3))
    return out


def parse_delimited(text, sep):
    """Rows of a TSV (Excel clipboard) or CSV text, with Excel's quoting."""
    rows, row, v, quoted, i, n = [], [], [], False, 0, len(text)
    if text[:1] == u'﻿':
        i = 1
    while i < n:
        c = text[i]
        if quoted:
            if c == u'"':
                if text[i + 1:i + 2] == u'"':
                    v.append(u'"')
                    i += 1
                else:
                    quoted = False
            else:
                v.append(c)
        elif c == u'"' and not v:
            quoted = True
        elif c == sep:
            row.append(u''.join(v))
            v = []
        elif c in u'\r\n':
            if c == u'\r' and text[i + 1:i + 2] == u'\n':
                i += 1
            row.append(u''.join(v))
            rows.append(row)
            row, v = [], []
        else:
            v.append(c)
        i += 1
    if v or row:
        row.append(u''.join(v))
        rows.append(row)
    return rows


def read_csv(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        text = f.read()
    first = text.split(u'\n', 1)[0]
    sep = u'\t' if u'\t' in first else u';' if first.count(u';') > first.count(u',') else u','
    return parse_delimited(text, sep)


def write_csv(path, rows):
    def cell(v):
        return u'"' + v.replace(u'"', u'""') + u'"' if re.search(u'[",\n;]', v) else v
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as f:
        f.write(u'\r\n'.join(u','.join(cell(v) for v in r) for r in rows))


# ---- xlsx -------------------------------------------------------------------------------

def _esc(s):
    s = re.sub(u'[\x00-\x08\x0b\x0c\x0e-\x1f]', u'', u'{0}'.format(s))
    return s.replace(u'&', u'&amp;').replace(u'<', u'&lt;').replace(u'>', u'&gt;').replace(u'"', u'&quot;')


def _col(i):
    s, i = u'', i + 1
    while i:
        i, r = divmod(i - 1, 26)
        s = unichr(65 + r) + s      # noqa: F821 (IronPython 2)
    return s


HEAD = u'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
MAIN = u'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
PKG = u'http://schemas.openxmlformats.org/package/2006/relationships'
DOCREL = u'http://schemas.openxmlformats.org/officeDocument/2006/relationships'


def write_xlsx(path, title, rows, numeric):
    """rows[0] = headings (bold, frozen, filter); column 0 (the element id) hidden; numeric[j] writes numbers."""
    widths = [min(60, max([8] + [len(r[j]) + 2 for r in rows[:300] if j < len(r)])) for j in range(len(rows[0]))]
    out = [HEAD, u'<worksheet xmlns="{0}"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" '
                 u'activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>'.format(MAIN)]
    for j, w in enumerate(widths):
        out.append(u'<col min="{0}" max="{0}" width="{1}" customWidth="1"{2}/>'.format(j + 1, w, u' hidden="1"' if j == 0 else u''))
    out.append(u'</cols><sheetData>')
    for i, r in enumerate(rows):
        out.append(u'<row r="{0}">'.format(i + 1))
        for j, v in enumerate(r):
            ref = u'{0}{1}'.format(_col(j), i + 1)
            if i and numeric[j] and re.match(r'^-?\d+(\.\d+)?$', v):
                out.append(u'<c r="{0}"><v>{1}</v></c>'.format(ref, v))
            elif v != u'':
                out.append(u'<c r="{0}" t="inlineStr"{1}><is><t xml:space="preserve">{2}</t></is></c>'.format(
                    ref, u' s="1"' if i == 0 else u'', _esc(v)))
        out.append(u'</row>')
    last = u'{0}{1}'.format(_col(len(rows[0]) - 1), len(rows))
    out.append(u'</sheetData><autoFilter ref="A1:{0}"/></worksheet>'.format(last))
    name = _esc(re.sub(r'[\\/?*\[\]:]', u' ', title)[:31] or u'Sheet1')
    parts = [
        (u'[Content_Types].xml', HEAD + u'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
         u'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
         u'<Default Extension="xml" ContentType="application/xml"/>'
         u'<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
         u'<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
         u'<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>'),
        (u'_rels/.rels', HEAD + u'<Relationships xmlns="{0}"><Relationship Id="rId1" Type="{1}/officeDocument" Target="xl/workbook.xml"/></Relationships>'.format(PKG, DOCREL)),
        (u'xl/workbook.xml', HEAD + u'<workbook xmlns="{0}" xmlns:r="{1}"><sheets><sheet name="{2}" sheetId="1" r:id="rId1"/></sheets></workbook>'.format(MAIN, DOCREL, name)),
        (u'xl/_rels/workbook.xml.rels', HEAD + u'<Relationships xmlns="{0}"><Relationship Id="rId1" Type="{1}/worksheet" Target="worksheets/sheet1.xml"/>'
         u'<Relationship Id="rId2" Type="{1}/styles" Target="styles.xml"/></Relationships>'.format(PKG, DOCREL)),
        (u'xl/styles.xml', HEAD + u'<styleSheet xmlns="{0}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>'
         u'<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders>'
         u'<cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="2"><xf/><xf fontId="1" applyFont="1"/></cellXfs></styleSheet>'.format(MAIN)),
        (u'xl/worksheets/sheet1.xml', u''.join(out)),
    ]
    fs = FileStream(path, FileMode.Create)
    try:
        zf = ZipArchive(fs, ZipArchiveMode.Create)
        for n, text in parts:
            w = StreamWriter(zf.CreateEntry(n).Open(), UTF8Encoding(False))
            w.Write(text)
            w.Dispose()
        zf.Dispose()
    finally:
        fs.Dispose()


def _entry(zf, name):
    e = zf.GetEntry(name)
    if e is None:
        return None
    r = StreamReader(e.Open())
    try:
        return r.ReadToEnd()
    finally:
        r.Dispose()


def _xml(text):
    d = XmlDocument()
    d.LoadXml(text)
    return d


def _text_of(node):
    return u''.join(t.InnerText for t in node.GetElementsByTagName('t'))


def read_xlsx(path):
    """Rows of the first worksheet as text."""
    fs = FileStream(path, FileMode.Open, FileAccess.Read)
    try:
        zf = ZipArchive(fs, ZipArchiveMode.Read)
        sheet = u'xl/worksheets/sheet1.xml'
        wb, rels = _entry(zf, u'xl/workbook.xml'), _entry(zf, u'xl/_rels/workbook.xml.rels')
        if wb and rels:
            first = _xml(wb).GetElementsByTagName('sheet')
            if first.Count:
                rid = first[0].GetAttribute('id', DOCREL)
                for r in _xml(rels).GetElementsByTagName('Relationship'):
                    if r.GetAttribute('Id') == rid:
                        t = r.GetAttribute('Target')
                        sheet = t[1:] if t.startswith(u'/') else u'xl/' + t
        sst = _entry(zf, u'xl/sharedStrings.xml')
        strings = [_text_of(si) for si in _xml(sst).GetElementsByTagName('si')] if sst else []
        text = _entry(zf, sheet)
        if not text:
            raise ValueError(u'không thấy sheet đầu tiên')
        zf.Dispose()
    finally:
        fs.Dispose()
    out = []
    for row in _xml(text).GetElementsByTagName('row'):
        r = int(row.GetAttribute('r') or (len(out) + 1)) - 1
        line = []
        for c in row.GetElementsByTagName('c'):
            ref, t = c.GetAttribute('r'), c.GetAttribute('t')
            j = 0
            for ch in re.sub(r'\d+', u'', ref):
                j = j * 26 + ord(ch) - 64
            vs = c.GetElementsByTagName('v')
            raw = vs[0].InnerText if vs.Count else u''
            if t == 's':
                v = strings[int(raw)]
            elif t == 'inlineStr':
                v = _text_of(c)
            elif t == 'b':
                v = u'Yes' if raw == u'1' else u'No'
            else:
                v = raw
                if t != 'str' and re.match(r'^-?\d+\.\d{9,}$', v):
                    v = u'{0}'.format(round(float(v), 6))
            while len(line) < j:
                line.append(u'')
            line[j - 1] = v
        while len(out) <= r:
            out.append([])
        out[r] = line
    return out
