# 01 · Dim each opening (dim từng cửa)

**Skill** `drafting-opening-dims` · **Standard** `drafting-opening-dims.md` Q1–Q5 · **Tools** `opening_dims_each`, `dims_edit`
**Applies to** every elevation / section of the job, one view per call, **opening by opening** (the user's rule).

## Do

For each listed opening, one call:
```
opening_dims_each {viewId, mode:"apply", openingId, dimTypeName:<profile check type>, nominalFamilies:<profile roll-up family>, cachePath, logPath}
```

It adds only what that opening is missing, on the nearest clear line beside it: V host level → bottom → top; H nearest grid → L → R → nearest grid, just above the head or below the sill, inside the storey. It also handles:
- **own partial V pieces** (check type) → replaced by one full chain;
- **side-by-side openings** (same sill / head, touching): one group, the H chain gets every joint (an existing chain missing a joint is completed in place), one V chain serves all;
- **stacked openings** (louvre over window): one shared H chain; the upper continues the lower V chain on the same line;
- **leaf vs frame**: a black dim within 60 mm of the frame top counts (2600 leaf vs 2650 frame).

## Done when

- Re-audit: `MissingV` = `MissingH` = 0 on the view.
- Bad placement (many clashes, outside the crop, no grid on one side) → that dim deleted (`dims_edit`) and listed for manual placement.

## Skip only if

No opening listed by the audit.

## Never

- Dim a hidden opening; a vertical dim across its own opening; a dim pushed into another storey; a link reference.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
