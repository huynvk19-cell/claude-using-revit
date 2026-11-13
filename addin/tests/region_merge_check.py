#!/usr/bin/env python3
"""Checks the plain-Python half of addin/extension/lib/region_merge.py (the half that
turns WPF's merged outline into filled-region loops). The WPF half needs Windows."""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "extension", "lib"))
import region_merge as rm  # noqa: E402

fails = []


def check(name, ok):
    if not ok:
        fails.append(name)


sq = [(0, 0), (4, 0), (4, 4), (0, 4)]
check("area ccw", rm.signed_area(sq) == 16)
check("area cw", rm.signed_area(sq[::-1]) == -16)
check("oriented", rm.signed_area(rm.oriented(sq, False)) == -16 and rm.oriented(sq, True) is sq)

# simplify: points on straight edges and a closing duplicate go, corners stay
dense = []
for a, b in zip(sq, sq[1:] + sq[:1]):
    dense += [(a[0] + (b[0] - a[0]) * i / 10.0, a[1] + (b[1] - a[1]) * i / 10.0) for i in range(10)]
out = rm.simplify(dense + dense[:1], 0.01)
check("simplify square", sorted(out) == sorted([(float(x), float(y)) for x, y in sq]))

# simplify: a circle keeps its shape within eps and loses most points
circle = [(math.cos(2 * math.pi * i / 720), math.sin(2 * math.pi * i / 720)) for i in range(720)]
out = rm.simplify(circle, 0.01)
check("simplify circle count", 8 <= len(out) <= 40)
check("simplify circle area", abs(abs(rm.signed_area(out)) - math.pi) < 0.06)
check("simplify keeps order", rm.signed_area(out) > 0)
check("simplify tiny", rm.simplify([(0, 0), (1, 0), (1, 0), (0, 1), (0, 0)], 0.1) == [(0, 0), (1, 0), (0, 1)])

# groups: outer + hole + island in the hole + a separate patch
hole = [(1, 1), (3, 1), (3, 3), (1, 3)]
island = [(1.5, 1.5), (2.5, 1.5), (2.5, 2.5), (1.5, 2.5)]
far = [(10, 0), (11, 0), (11, 1), (10, 1)]
gs = rm.groups([island, far, hole, sq])
check("groups count", len(gs) == 3)
check("groups nesting", [sq, hole] in gs and [island] in gs and [far] in gs)

if fails:
    print("region_merge_check FAILED: " + ", ".join(fails))
    sys.exit(1)
print("region_merge_check OK")
