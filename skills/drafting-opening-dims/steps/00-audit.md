# 00 · Audit (khảo sát dim cửa)

**Skill** `drafting-opening-dims` · **Standard** `drafting-opening-dims.md` Q1–Q5 · **Tools** `opening_dims_each`
**Applies to** every elevation / section of the job, one view per call, **opening by opening** (the user's rule).

## Do

```
opening_dims_each {viewId, mode:"audit", dimTypeName:<profile check type>, cachePath:<review>/each-<view>-cache.json}
```

Read-only, 1–2 min per view. It finds the openings really visible, checks **by reference to the opening** whether each has a V chain (host level / sill / head) and an H chain (both edges), and lists only the openings with gaps. Never trust a row / batch result that counts a dim as existing by position.

## Done when

- Every view audited, its cache written; the list of openings with gaps (`MissingV`, `MissingH`).

## Skip only if

The view shows no opening (audit: 0 visible).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
