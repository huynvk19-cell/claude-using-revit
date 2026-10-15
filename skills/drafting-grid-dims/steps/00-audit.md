# 00 · Audit (khảo sát dim trục)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` G1–G4 · **Tools** `grid_dims_band`
**Applies to** every view of the job; plans may go many per call, elevations / sections one view per call.

## Do

```
grid_dims_band {mode:"audit", sheetPrefix:<profile prefix>, dimType, preferSides, excludeNameContains, onlyProblems:true}
```

Each group comes back with `Side` (chosen and why), `BandMm` (room available vs needed), `Chain` / `Overall` (kept dims and where each sits: `in band`, `inside crop`, `beyond bubble`, `on the other side`), `Issues`; each view with `AnnoCrop` (`BubbleEndsOutside`, `Need` per side).

Show one table: view name → group → issues, then sort the work:

| Kind | Examples | Action |
|---|---|---|
| Automatic | missing chain/overall, kept dim outside the band, annotation crop too small | card 01 with defaults |
| Needs a yes | extra dims (`deleteExtra`); band too narrow, grid ends inside the crop (`extendGrids`, mm per side) | ask, then card 01 with the flag |
| Manual | crop must shrink but a Scope Box locks it; curved grids; no room on any side | Việc tồn |

## Done when

- Every view audited; each view's work sorted into automatic / needs a yes / manual; the yes asked.

## Skip only if

The view is in the profile exclusions, or its template hides Dimensions (`DimensionsHidden`).
