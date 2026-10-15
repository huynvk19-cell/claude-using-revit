# 03 · Recheck (kiểm lại)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` ĐỦ – ĐÚNG – ĐẸP · **Tools** `grid_dims_band`, `annotation_overlaps`, `export_sheet_images`
**Applies to** every view of the job; plans may go many per call, elevations / sections one view per call.

## Do

1. Audit again on the fixed views until `OK`.
2. `annotation_overlaps` per view.
3. Export the sheet, look at the band and the bubbles (`drafting-visual-check`).
4. Annotation crop grew → check the viewport against its neighbours and the title block; re-place titles (`drafting-tags-titles`).
5. Report per `drafting-session`: groups OK / fixed / left per view, annotation crop growth (paper mm), grid ends extended; **Cần xem** (tight bands, sides without bubbles, dims shared between dependents); **Việc tồn** (manual crops, scope boxes).

Undo one view: `grid_dims_band {mode:"undo", logPath}`.

## Done when

- Audit `OK` on every view, overlaps = 0.

## Skip only if

Never.
