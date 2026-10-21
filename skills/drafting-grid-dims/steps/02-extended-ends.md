# 02 · After grid ends were extended (sau khi kéo đầu trục)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` G3 · **Tools** `viewport_titles_place`, `export_sheet_images`, `grid_dims_band`
**Applies to** every view of the job; plans may go many per call, elevations / sections one view per call.

## Do

For views where the fix reported `ExtendedSides`:
- Move the titles below the new bubble row: `viewport_titles_place {mode:"apply", sheetNumbers, onlyViewports:[vp], ignoreCrop:true, logPath}`.
- Export the sheet and look. Bubbles or dims landing on another viewport or outside the sheet border → `undo` that view, redo with `forceSides` (e.g. `{horizontal:"right"}`) on a side with room; no side works → Việc tồn.

## Done when

- No bubble or dim on another viewport or outside the border; titles below the bubbles.

## Skip only if

No grid end was extended on the view.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
