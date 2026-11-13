# -*- coding: utf-8 -*-
"""What is picked in a pick list (lib/pick_list.py), without any window: plain Python, so
addin/tests/pick_model_check.py runs it outside Revit.

Items are known by their index k. A row is a mapping made by the `row` factory (a .NET
dictionary in the window, a dict in the test) with: kind "s" (item) / "g" (group), k, no,
name, count, arrow, indent, state "0" / "1" (part of a group) / "2".
"""
import re

ARROW_OPEN, ARROW_SHUT = u"", u""
OPEN_UP_TO = 60                     # more items than this: the groups start folded
SHOW, CHANGED = "show", "changed"   # what the window redraws after a press


def parse_terms(text):
    """Commas / lines / tabs: any of them; spaces in one: all of its words."""
    return [t.split() for t in re.split(u"[,;\\r\\n\\t]+", (text or u"").lower()) if t.strip()]


class PickModel(object):
    def __init__(self, labels, on=None, row=dict, single=False, keys=None):
        """labels: [(no, name, note)] per item; keys: the text the filter looks in (default no + name)."""
        self.n = len(labels)
        self.on = [bool(v) for v in on] if on is not None else [False] * self.n
        self.keys = [t.lower() for t in keys] if keys else [(no + u" " + name).lower() for no, name, _ in labels]
        self.row, self.single = row, single
        self.rows = [row(kind="s", k=k, no=no, name=name, count=note, arrow=u"", indent=6,
                         state="2" if self.on[k] else "0") for k, (no, name, note) in enumerate(labels)]
        self.terms, self.only_on = [], False
        self.match, self.shown, self.where = set(range(self.n)), [], {}
        self.members, self.group_rows, self.closed = {}, [], set()
        self.anchor, self.dragging, self.paint, self.before, self.last = None, False, True, [], None

    # ---- groups ---------------------------------------------------------

    def set_groups(self, paths):
        """paths: one group label per item, or None. One group or none: a flat list."""
        members = {}
        for k, g in enumerate(paths or []):
            members.setdefault(g, []).append(k)
        if len(members) < 2:
            members = {}
        self.members = members
        seen = sorted(members, key=lambda g: members[g][0])
        self.group_rows = [self.row(kind="g", k=-1, no=g, name=u"", count=u"", arrow=ARROW_OPEN,
                                    indent=18, state="0") for g in seen]
        self.closed &= set(members)

    def fold_all(self, shut=True):
        self.closed = set(self.members) if shut else set()

    # ---- what is shown ----------------------------------------------------

    @property
    def filtering(self):
        return bool(self.terms or self.only_on)

    def _matches(self, k):
        if self.only_on and not self.on[k]:
            return False
        if not self.terms:
            return True
        text = self.keys[k]
        return any(all(w in text for w in term) for term in self.terms)

    def rebuild(self):
        """What the list shows, from the filter and the folded groups."""
        self.match = set(k for k in range(self.n) if self._matches(k))
        shown = []
        if self.group_rows:
            for g in self.group_rows:
                hits = [k for k in self.members[g["no"]] if k in self.match]
                if not hits:
                    continue
                shown.append(g)
                if self.filtering or g["no"] not in self.closed:
                    shown += [self.rows[k] for k in hits]
        else:
            shown = [self.rows[k] for k in sorted(self.match)]
        self.shown = shown
        self.where = dict((r["k"], i) for i, r in enumerate(shown) if r["kind"] == "s")
        self.paint_rows()

    def paint_rows(self):
        for k, r in enumerate(self.rows):
            r["state"] = "2" if self.on[k] else "0"
        for g in self.group_rows:
            m = self.members[g["no"]]
            n = len([k for k in m if self.on[k]])
            g["state"] = "2" if n == len(m) else "1" if n else "0"
            g["count"] = u"{0} / {1}".format(n, len(m))
            g["arrow"] = ARROW_OPEN if self.filtering or g["no"] not in self.closed else ARROW_SHUT

    # ---- picking ------------------------------------------------------------
    # Press = toggle and start a drag; dragging over rows sets them all like the first one
    # (moving back gives rows back their state); Shift+press = the whole range from the last
    # pressed row. A group's box picks the whole group, the rest of its row folds it.

    def press(self, row, tag=None, shift=False):
        """A press on a shown row; tag "box" = on its tick box. Returns SHOW or CHANGED."""
        if row["kind"] == "g":
            m = self.members[row["no"]]
            if tag == "box" and not self.single:
                v = not all(self.on[k] for k in m)
                for k in m:
                    self.on[k] = v
                return CHANGED
            self.closed.symmetric_difference_update([row["no"]])
            return SHOW
        k = row["k"]
        if self.single:
            v = not self.on[k]
            self.on = [False] * self.n
            self.on[k] = v
            return CHANGED
        if shift and self.anchor in self.where:
            self.before = list(self.on)
            self.paint = self.on[self.anchor]
            self.range_to(k)
            return CHANGED
        self.anchor = k
        self.paint = not self.on[k]
        self.before = list(self.on)
        self.dragging = True
        self.last = None
        self.range_to(k)
        return CHANGED

    def range_to(self, k):
        """Rows shown from the anchor to k take the first row's state, others go back.
        False: nothing new."""
        if k == self.last or k not in self.where or self.anchor not in self.where:
            return False
        self.last = k
        lo, hi = sorted((self.where[self.anchor], self.where[k]))
        self.on = list(self.before)
        for r in self.shown[lo:hi + 1]:
            if r["kind"] == "s":
                self.on[r["k"]] = self.paint
        return True

    def set_matching(self, value):
        """Every item the filter lets through, also those in folded groups."""
        if self.single:
            return
        for k in self.match:
            self.on[k] = bool(value)

    def set_on(self, on):
        self.on = [bool(v) for v in on]

    def all_matching_on(self):
        return bool(self.match) and all(self.on[k] for k in self.match)

    def picked(self):
        return [k for k in range(self.n) if self.on[k]]
