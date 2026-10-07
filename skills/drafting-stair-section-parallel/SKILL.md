---
name: drafting-stair-section-parallel
description: "Detail a stair SECTION cut parallel to the stair path (longitudinal section along the flights, NOT a plan, NOT a cross-section) via Revit MCP, rules LA–LC: flight-height dims '169.4mm x 16R = 2710 (EQUAL RISERS)', level-to-level dims, flight-going dims '280mm x 15T = 4200 (EQUAL TREADS)', clear height under landings, handrail height, wall/door chains to grids; run tags 'From EL … To EL … / 169.4mm x 16R', railing tags, spot elevations on every landing, floor finish (F..) and landing soffit (C..) tags, door tags; riser numbers continuous over the stair, from the model. Use for mặt cắt thang, mặt cắt dọc thang, mặt cắt cầu thang, chi tiết mặt cắt thang, dim chiều cao vế, EQUAL RISERS, cổ bậc, tag vế trên mặt cắt, staircase section, stair section."
---

# Stair section parallel to the stair path (LA–LC)

**Section views cut along the flights only.** Plans → `drafting-stair-plan`. A section cut across the flights is another topic (not written yet): stop and say so.

Standard: `~/.claude/drafting-domain/drafting-stair-section-parallel.md` (Vietnamese). Its rules and its "Mẫu tham chiếu" decide every case. Read it first. Open `drafting-opening-tags.md` only if the section shows doors or windows (LB6).

Load `drafting-session` first. From the project `drafting-profile.md` take the **check** dim type, and whether LA4 carries `CLEAR` (default: no, as the sample).

Section views: **one view per call**, with `timeoutSeconds`.

## 1. Read the flights (read-only)

```
stair_section_info {viewId, outPath:"<review>/stair-sec-<viewId>.json"}
```

- `Flights`: F1, F2… going up; `State` (cut / beyond), `Rises` (left / right), `TagText`, `RiseText` (`169.4mm x 16R = 2710`), `GoingText` (`280mm x 15T = 4200`), run tags, riser numbers. **Never count risers from the drawing.**
- `Landings`: elevation and side.
- `Dims`: each flight's rise (LA1) and going (LA3) dim, `OK` / `missing` / wrong text.
- `RunTagTypes`: the most-used run tag types in sections and elsewhere.
- `Issues`, `Notes`. A note that flights run toward the viewer → this is a cross-section: stop.

Show the user one table: flight → state → risers × height → treads × depth → tag text; then the issues by rule.

Stop and ask when: no host stairs (link); `LC:` model mismatch; no run tag type used in sections.

## 2. Dims

1. **LA2 levels**: `level_dims_add {viewId, side:<opposite of LA1>, typeName:<check type>, mode:"preview", logPath}` → same with `mode:"apply"`. Skipped if the view already has a level chain.
2. **LA1, LA3, LA4, LA5, LA6**: no batch tool yet. Write a one-off dynamic command per view (preview → apply with `logPath`; undo = delete the logged ids with `dims_edit`). References:
   | Dim | References |
   |---|---|
   | LA1 rise chain | top faces of the landings / floors (`HostObjectUtils.GetTopFaces` for floors; landing geometry with `Options{ComputeReferences=true, View=v}`), or the level when the finish floor sits on it |
   | LA3 going chain | wall finish faces, first / last riser faces of the cut flight (vertical planar faces normal to the view's right direction) |
   | LA4 clear height | landing top face → soffit face of the landing / flight above |
   | LA5 handrail | nosing line or landing top → top face of the handrail (`railing.TopRail` / `GetHandRails()` geometry) |
   | LA6 | `new Reference(grid)`, wall side faces, door `FamilyInstanceReferenceType.Left / Right` |
   Placement per the standard's table: LA1 outside one wall, LA2 outside the other, LA3 at mid-height of each cut flight, LA4 inside the core next to the walls, LA6 below the section.
3. **Text**: set the formulas with `dims_text`:
   ```
   dims_text {viewId, mode:"apply", logPath, items:[
     {dimId, valueMm:2710, prefix:"169.4mm x 16R = ", below:"(EQUAL RISERS)"},
     {dimId, valueMm:4200, prefix:"280mm x 15T = ",  below:"(EQUAL TREADS)"}]}
   ```
   Take the text from `RiseText` / `GoingText`. One spelling for the project.

## 3. Tags and numbers

- **LB1** run tag on every seen flight (cut and beyond): `IndependentTag.Create(doc, type, viewId, new Reference(run), true, Horizontal, head)`. Put the head in the free space under or beside the flight, with a short leader into it. Check the tag reads `From EL … To EL … / …R`.
- **LB2** railing `P01`, **LB3** spot elevation on every landing / floor top at each end (`doc.Create.NewSpotElevation`), **LB4** `F..` above each landing with its leader down, **LB5** `C..` under each landing with its leader up, in the same column as LB4.
- **LB6** doors/windows: `elevation_opening_tags {viewIds:[id], mode:"preview"}` → `apply`, rules T1–T8.
- **LC** riser numbers on each cut flight: `NumberSystem.Create(doc, viewId, new LinkElementId(runId), StairsNumberSystemReferenceOption.Left, new LinkElementId(typeId))`. The numbers continue over the whole stair (every second one shown) and must match the plan at the same riser. Wrong start → fix it by hand or ask.

## 4. Verify and report

1. `stair_section_info` again → every `Dims` row `OK`, no `Issues` left except those the user accepted.
2. `annotation_overlaps {viewId}`.
3. Export the sheet image and compare with the standard's sample (`drafting-visual-check`).
4. Report per `drafting-session`: exact view name; table per flight (tag, rise dim, going dim, numbers); **Cần xem** (unreadable sample values, LA4 CLEAR yes/no); **Việc tồn**.

## Never

- Count risers or treads from the drawing.
- Replace a dim value with text (use prefix / below).
- Add a stair path to a section.
- Put LA1 and LA2 on the same side, or dim one chain on both sides.
- Number the risers of a flight seen beyond.
- Edit stairs, railings, floors or walls (R1).
