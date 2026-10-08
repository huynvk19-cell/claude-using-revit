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

## 2. Dims, tags, numbers: `stair_section_annotate` (one view)

```
stair_section_annotate {viewId, mode:"preview", dimTypeName:<check type>, la1LineMm,
  runTagTypes:[{fromMm, type}], spotTypeName, numberSourceId:<a finished NumberSystem>,
  parts:["LA1","LA3","LA4","LB1","LB3","LC"], items:[{part, flight|landing, x, z}]}
```
Read `Done` (texts, values, positions) → same with `mode:"apply", logPath` → `view_elem_boxes` on the new ids (Box ≠ null) → export the view image. Undo: `mode:"undo", logPath`.

- References are taken on the **Stairs** element (run / landing 3D faces are hidden: dims on them are created but not drawn); level plane where no landing.
- Formula text on riser numbers or on the flight → `dims_text_move` (alongMm) into the free space under the flight, clear of the witness lines.
- Layout that worked (1:50): LA3 line at 30 % of the flight height, run tag head at 8 % with a horizontal leader to the soffit; LA1 300 mm inside an existing overall dim; spots on the landing away from the F.. box.
- **LA5 is MANDATORY on every cut landing** (user, 2026-10-08): landing top → top of the rail along the landing edge (P02 → 1200). `la5Auto:true, la5RailingIds, la5TargetMm:1200, la5LeftX, la5RightX` finds the rail reference without a sample; values far from the nominal height → check with `rail_top_at`, do not place when unsure, ask the user for one hand dim (`la5FromDimId`). `stair_section_info` lists every landing without it — never report a section done while one is missing. Rails often have no straight line in the API geometry: ask the user for ONE hand dim, then `parts:["LA5"], la5FromDimId:<that dim>, la5IndexFrom, la5IndexTo, items:[{part:"LA5", landing, x}]` reuses its rail reference with other indices until the value matches and the dim is drawn. Landings listed with `x` only; `skip:true` for the ones the user dimmed.
- **Wall handrail P01 seen along a landing** (a horizontal run on the wall, not hidden by P02): add a second rail dim on every cut landing, landing top → top of the P01 **handrail** (`900`), next to the 1200 dim (230–300 mm toward the wall, or ~500 mm from the wall finish on the other side). `la5Auto` only reads top rails: take the P01 handrail reference from a hand dim (`la5FromDimId`, `la5TargetMm:900`) or ask for one.
- **LB2** railing tags (skip a rail hidden behind a bigger one, e.g. a wall handrail behind the 1200 railing): `annot_place` (kind tag, the railing tag type used in the project, orthogonal leader); view-frame `up` = elevation + the frame offset of the view (a level's witness in `view_dims_snapshot`). A P01 seen along a landing gets its own tag (head ~230 mm above the rail, vertical leader), on the landings without a P02 tag nearby.
- **LB1** tags and **LC** numbers only on **cut** flights (user, 2026-10-08). **LA3** counts T = R − 1 (top tread level with the landing belongs to the landing segment), as the plan.

The manual reference notes below stay for cases the tool does not cover:

1. **LA2 levels**: `level_dims_add {viewId, side:<opposite of LA1>, typeName:<check type>, mode:"preview", logPath}` → same with `mode:"apply"`. Skipped if the view already has a level chain.
2. **LA1, LA3, LA4, LA5, LA6** references:
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

- **LB1** run tag on every **cut** flight: `IndependentTag.Create(doc, type, viewId, new Reference(run), true, Horizontal, head)`. Put the head in the free space under the flight, with a short leader into it. Check the tag reads `From EL … To EL … / …R`.
- **LB2** railing `P01`, **LB3** spot elevation on every landing / floor top at each end (`doc.Create.NewSpotElevation`), **LB4** `F..` above each landing with its leader down, **LB5** `C..` under each landing with its leader up, in the same column as LB4.
- **LB6** doors/windows: `elevation_opening_tags {viewIds:[id], mode:"preview", addLeader:true}` → `apply`, rules T1–T8. Every door tag in a stair section has a leader. The tool sees only doors facing the view: list the doors of the walls the section cuts (the core's rooms / levels) and tag each one it missed with `annot_place` — head outside the wall, horizontal leader into the door at about half its height.
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
