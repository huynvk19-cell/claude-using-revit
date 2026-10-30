# -*- coding: utf-8 -*-
"""Status on the Revit ribbon tab "claude using revit": what each tool has left to do in the active model.

- Tab: Revit's own highlight dot on the tab header while any tool has work left.
- Panel: a column of short status lines at the right end of the panel (full detail in the tooltip);
  a panel with work left gets an orange title bar, a done one stays plain.
- Button: the highlight dot on the button that does the work (Print set theo DM, Chuẩn cột danh mục,
  Tìm chưa tag / Xoá vùng tô, Xoá highlight).
Same rules as the web app; the data is the read-only task status_board, computed on Revit's Idling
(never inside another command) and kept light: once when another model becomes active, and after one of
our own tools changed the model. Opening views and ordinary edits never trigger a check. Started by startup.py.
Only AdWindows display properties and our own status items (ids "CUR_STATUS_*") are touched: panel and
tab titles stay as pyRevit made them, so pyRevit > Reload finds them.
"""
import time

import clr
clr.AddReference('AdWindows')
clr.AddReference('PresentationCore')
import Autodesk.Windows as aw
from Autodesk.Internal.Windows import HighlightMode
from System import AppDomain, Enum
from System.Windows.Media import Color, SolidColorBrush

import web_bridge

SLOT = 'ClaudeUsingRevit.RibbonStatus'
TAB = u'claude using revit'
ID = 'CUR_STATUS_'
NO_HIGHLIGHT = Enum.Parse(HighlightMode, 'None')
WORK_BAR = SolidColorBrush(Color.FromArgb(255, 255, 159, 10))
KIND = {'Room': u'room', 'Door': u'cửa đi', 'Window': u'cửa sổ', 'Wall': u'tường', 'Floor': u'sàn'}


def _log(text=None):
    import os
    import traceback
    try:
        with open(os.path.join(os.environ.get('APPDATA', ''), 'claude-using-revit', 'ribbon_status.log'), 'a') as f:
            f.write((text or traceback.format_exc()) + '\n')
    except Exception:
        pass


# ---- what is left (same rules as webapp/public/app.js) -----------------------------------------

def _official(b):
    """The drawing lists that count: the ones with a print set of their name, plus the ones this
    user works with (status_board 'expected': last used in Print set theo DM / Chuẩn cột danh mục)."""
    named = set(r['schedule'] for r in b.get('printSets', []) if r['setExists']) | set(b.get('expected') or [])
    return [r for r in b.get('columns', []) if r['schedule'] in named]


def _short(text, n=38):
    return text if len(text) <= n else text[:n - 1] + u'…'


def lines_for(b):
    """{panel title: [(level work|done|idle, short line, tooltip, button keys with work)]}"""
    out = {u'In ấn': [], u'Kiểm tra tag': [], u'Đánh dấu': []}
    if not b:
        return out
    sets = b.get('printSets', [])
    off = [r for r in sets if r['setExists'] and not r['inSync']]
    missing = [r for r in sets if not r['setExists'] and r['schedule'] in (b.get('expected') or []) and r['sheets']]
    if missing:
        out[u'In ấn'].append(('work', _short(u'● Thiếu print set {0}'.format(missing[0]['schedule'])) if len(missing) == 1 else u'● Thiếu {0} print set'.format(len(missing)),
                              u'\n'.join(u'Danh mục {0} chưa có print set cùng tên.'.format(r['schedule']) for r in missing)
                              + u'\nBấm Print set theo DM để tạo.', ['printsetsync']))
    if off:
        r = off[0]
        short = u'● Print set lệch +{0} −{1}'.format(r['add'], r['remove']) if len(off) == 1 else u'● {0} print set lệch'.format(len(off))
        tip = u'\n'.join(u'{0}: +{1} sheet, −{2} sheet so với danh mục'.format(x['schedule'], x['add'], x['remove']) for x in off)
        out[u'In ấn'].append(('work', short, tip + u'\nBấm Print set theo DM để đồng bộ.', ['printsetsync']))
    elif any(r['setExists'] for r in sets) and not missing:
        out[u'In ấn'].append(('done', u'✓ Print set khớp DM', u'Mọi print set mang tên danh mục đều khớp.', []))
    rows = _official(b)
    bad = [r for r in rows if not r.get('inSync') and not r.get('error')]
    if bad:
        tip = u'\n'.join(u'{0}: thiếu {1} cột, {2} số No.'.format(r['schedule'], r.get('addColumns', 0), r.get('numbersToWrite', 0)) for r in bad)
        out[u'In ấn'].append(('work', u'● {0} DM chưa chuẩn cột'.format(len(bad)), tip + u'\nBấm Chuẩn cột danh mục.', ['sheetlistcolumns']))
    elif rows:
        out[u'In ấn'].append(('done', u'✓ Cột DM đúng chuẩn', u'Các danh mục đều đủ cột chuẩn và số No.', []))
    if 'untagged' in b:
        u = b.get('untagged')
        left = [(k, n) for k, n in (u['counts'].items() if u else []) if n]
        if left:
            text = u', '.join(u'{0} {1}'.format(n, KIND.get(k, k)) for k, n in left)
            out[u'Kiểm tra tag'].append(('work', u'● ' + (text if len(text) <= 26 else u'{0} chỗ'.format(sum(n for _, n in left))) + u' chưa tag',
                                         u'{0}: {1} chưa tag.\nBấm Tìm chưa tag.'.format(u['view'], text), ['highlightuntagged']))
        elif u:
            out[u'Kiểm tra tag'].append(('done', u'✓ View đã tag đủ', u'{0}: không còn room, cửa, tường, sàn nào chưa tag.'.format(u['view']), []))
        else:
            out[u'Kiểm tra tag'].append(('idle', u'View này không kiểm tag', u'Mở một mặt bằng, mặt cắt hay mặt đứng.', []))
    if b.get('untaggedMarks'):
        out[u'Kiểm tra tag'].append(('work', u'● Còn {0} vùng tô cam'.format(b['untaggedMarks']),
                                     u'Vùng tô cam chưa tag do bạn tạo vẫn còn trong model.\nXoá khi đã tag xong.', ['removeuntagged']))
    if b.get('highlights'):
        out[u'Đánh dấu'].append(('work', u'● Còn {0} vùng highlight'.format(b['highlights']),
                                 u'Highlight đỏ do bạn tạo vẫn còn trong model.\nBấm Xoá vùng tô khi xong.', ['clearhighlight']))
    elif 'highlights' in b:
        out[u'Đánh dấu'].append(('idle', u'Không còn highlight', u'Không còn highlight nào của bạn trong model.', []))
    return out


# ---- the ribbon ----------------------------------------------------------------------------------

def _tab():
    for t in aw.ComponentManager.Ribbon.Tabs:
        if t.Title == TAB:
            return t
    return None


def _walk(items):
    for it in items:
        yield it
        sub = getattr(it, 'Items', None)
        if sub is not None and not isinstance(it, aw.RibbonPanelSource):
            for x in _walk(sub):
                yield x


def _key(item):
    return (u'{0}'.format(item.Id or u'')).lower().replace(u' ', u'').replace(u'-', u'').replace(u'_', u'')


def paint(board):
    tab = _tab()
    if tab is None:
        return
    per_panel = lines_for(board)
    any_work = False
    for panel in tab.Panels:
        src = panel.Source
        if src is None:
            continue
        lines = per_panel.get(src.Title, [])
        work = [l for l in lines if l[0] == 'work']
        any_work = any_work or bool(work)
        panel.CustomPanelTitleBarBackground = WORK_BAR if work else None
        hot = set(k for l in work for k in l[3])
        for it in _walk(src.Items):
            if isinstance(it, aw.RibbonButton):
                k = _key(it)       # pyRevit button ids hold the bundle name, e.g. ...printsetsync
                it.Highlight = HighlightMode.New if any(h in k for h in hot) else NO_HIGHLIGHT
        _status_items(src, lines)
    tab.Highlight = HighlightMode.New if any_work else NO_HIGHLIGHT


def _status_items(src, lines):
    """Our status column at the end of the panel: replaced each time, removed when empty."""
    for it in list(src.Items):
        if (it.Id or u'').startswith(ID):
            src.Items.Remove(it)
    if not lines:
        return
    sep = aw.RibbonSeparator()
    sep.Id = ID + 'sep_' + src.Title
    src.Items.Add(sep)
    row = aw.RibbonRowPanel()
    row.Id = ID + 'row_' + src.Title
    for i, (level, text, tip, _) in enumerate(lines[:3]):
        if i:
            row.Items.Add(aw.RibbonRowBreak())
        lab = aw.RibbonLabel()
        lab.Id = ID + 'lbl_{0}_{1}'.format(src.Title, i)
        lab.Text = text
        lab.ShowText = True
        lab.ToolTip = tip
        lab.IsToolTipEnabled = True
        row.Items.Add(lab)
    src.Items.Add(row)


def clear():
    tab = _tab()
    if tab is None:
        return
    for panel in tab.Panels:
        if panel.Source is not None:
            panel.CustomPanelTitleBarBackground = None
            _status_items(panel.Source, [])
            for it in _walk(panel.Source.Items):
                if isinstance(it, aw.RibbonButton):
                    it.Highlight = NO_HIGHLIGHT
    tab.Highlight = NO_HIGHLIGHT


# ---- when to check ---------------------------------------------------------------------------------

class Watcher(object):
    """Light on purpose: it never checks when a view opens or on ordinary edits.
    Checks (on Revit's Idling, never inside another command):
      - lists + marks once when another model becomes active;
      - after one of OUR tools changed the model (its transaction name), the part that tool touches.
    Untagged counts come only from the Tìm chưa tag run itself (its transaction) for the active view."""

    def __init__(self, uiapp):
        self.uiapp = uiapp
        self.board = {}
        self.doc_key = None
        self.need = set(['lists', 'marks'])
        self.after = {}               # part -> time our tool changed it
        self.busy = False
        uiapp.ViewActivated += self.on_view
        uiapp.Application.DocumentChanged += self.on_change
        uiapp.Idling += self.on_idle

    def stop(self):
        try:
            self.uiapp.ViewActivated -= self.on_view
            self.uiapp.Application.DocumentChanged -= self.on_change
            self.uiapp.Idling -= self.on_idle
        except Exception:
            pass
        try:
            clear()
        except Exception:
            pass

    def on_view(self, sender, e):
        doc = e.Document
        key = doc.Title if doc else None
        if key != self.doc_key:          # another model: its own status (views of the same model: nothing)
            self.doc_key = key
            self.board = {}
            self.need = set(['lists', 'marks'])
        elif 'untagged' in self.board:   # that count was for the view left behind: drop it, no new check
            del self.board['untagged']
            try:
                paint(self.board)
            except Exception:
                _log()

    def on_change(self, sender, e):
        try:
            if e.GetDocument().Title != self.doc_key:
                return
            now = time.time()
            for name in e.GetTransactionNames():
                for part in TOOL_TRANSACTIONS.get(_tx_key(name), ()):
                    self.after[part] = now
        except Exception:
            _log()

    def on_idle(self, sender, e):
        if self.busy:
            return
        now = time.time()
        for p, t in list(self.after.items()):
            if now - t > 1.0:
                del self.after[p]
                self.need.add(p)
        if not self.need:
            return
        uiapp = sender if hasattr(sender, 'ActiveUIDocument') else self.uiapp
        uidoc = uiapp.ActiveUIDocument
        if uidoc is None or uidoc.Document.IsFamilyDocument:
            self.need = set()
            self.board = {}
            paint(None)
            return
        self.busy = True
        parts, self.need = list(self.need), set()
        try:
            res = web_bridge._tasks().run('status_board', {'parts': parts}, uidoc.Document, uidoc)
            self.board.update(res)
            paint(self.board)
        except Exception:
            _log()
        finally:
            self.busy = False


def _tx_key(name):
    n = (name or u'').lower()
    for k in sorted(TOOL_TRANSACTIONS, key=len, reverse=True):     # 'highlight untagged' before 'highlight'
        if n.startswith(k):
            return k
    return None


# transaction name (start, lower case) of our tools -> status parts to check again
TOOL_TRANSACTIONS = {
    u'print set ': ('lists',),
    u'chuẩn cột danh mục': ('lists',),
    u'đánh số no.': ('lists',),
    u'highlight untagged': ('marks', 'untagged'),
    u'remove untagged highlight': ('marks', 'untagged'),
    u'highlight': ('marks',),
    u'clear highlight': ('marks',),
}


def start(uiapp):
    old = AppDomain.CurrentDomain.GetData(SLOT)
    if old is not None:
        try:
            old.stop()
        except Exception:
            pass
    w = Watcher(uiapp)
    AppDomain.CurrentDomain.SetData(SLOT, w)
    try:
        uidoc = uiapp.ActiveUIDocument
        if uidoc is not None:
            w.doc_key = uidoc.Document.Title
            w.on_idle(uiapp, None)        # first paint now (pyRevit load runs in API context)
    except Exception:
        _log()
    return w
