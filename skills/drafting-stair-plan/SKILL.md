---
name: drafting-stair-plan
description: "Detail a stair core PLAN view (floor plan only, NOT a stair section) via Revit MCP, rules SA1–SD: clear widths bounded by the railing edge, else the step / landing edge, else the finish wall (suffix CLEAR only on the clear width of a run; landings and overalls as chains closed on the wall, no CLEAR), run-length dims with the formula 280mm x 14T = 3920 (EQUAL TREADS), the top tread at landing level not counted, plus the landing to the wall, wall/door/window chains to the grids in an outer layer; run tags outside the side walls with leaders on the 3 seen runs (V1 half beyond the cut, V2 full, V3 half before the cut), railings (P01/P02), spot elevations (landings, floor outside the stair door), doors/windows, landing and wall finishes (F.., W.. mandatory); tread counts per run from the model, riser numbers continuous over the whole stair; stair path with an arrow only (no UP/DOWN). Use for mặt bằng thang, mặt bằng lõi thang, mặt bằng thang bộ, dim thang trên mặt bằng, thông thuỷ vế thang, CLEAR, chiếu nghỉ trên mặt bằng, vế thang, đếm bậc, đánh số bậc, tag vế thang, tay vịn, stair path, mũi tên thang, stair core plan, staircase plan. Not for mặt cắt thang / stair sections."
---

# Stair core plan (SA1–SD)

**Plan views only.** Stair sections are a different job: along the flights → `drafting-stair-section-parallel`; across the flights → `drafting-stair-section-cross`. If the view is a section or elevation, stop and say so.

Standard: `~/.claude/drafting-domain/drafting-stair-plan.md` (Vietnamese). Its rules decide every case. Read it first. Its section "Mẫu tham chiếu" describes the user's sample sheet: match that layout.

Load `drafting-session` first. From the project `drafting-profile.md` take:
- the **check** dim type → new dims;
- stair core views (name filter) and whether tread numbers are shown (default: yes);
- tag / path type overrides, if the profile names any. Otherwise the project's most-used type per category.

Plan views only. **One view per call.** Tools have no project defaults.

## 1. Audit (read-only)

```
stair_plan_audit {viewId, excludeStairIds:[<stairs outside the core>], outPath:"<review>/stair-<viewId>.json"}
```

- **A neighbouring stair outside the core, partly in the crop**, breaks the audit (an extra lane, a side wall not found, every expectation shifted). Find it in `Runs` / `Lanes` (a lane beyond a core wall, a run cut by the crop) and re-run with `excludeStairIds`. Pass the same list to `stair_plan_annotate`.
- **V1 often belongs to the stair of the floor below and can sit under the View Depth** (its top = the floor = the view depth). Revit still draws every component of a stair the view collects; the audit counts a run / landing as seen when no higher run / landing covers it, and a below run under a cut run is V1. A typical floor shows **3 runs and 2 landings**: if the audit reports fewer while the view shows more, say so and check `view_range_info`, do not annotate a partial set.
- **Keep what the user already drew** (dims, tags, spots, numbers): add only what is missing, and copy their layout to the views still to do.

Read, in this order:
1. `Runs`: label **V1 / V2 / V3** (beyond cut / full / cut), treads, tread depth, `Text` (e.g. `280mm x 15T = 4200`), `ClearWidth` and what bounds it (`ClearFrom` / `ClearTo`). These numbers come from the model: **never count treads from the drawing**.
   - **Treads (user rule)**: the top tread that sits at the landing / floor level is not counted: `Treads` = risers − 1 when the model has as many treads as risers (`TopTreadAtLanding: true`, e.g. 16R → 15T). `RunLengthMm` = first riser → last riser; the flush top tread belongs to the landing segment.
   - **Clear width bounds (user rule)**: railing edge, else the step / landing edge, else the finish wall face. A landing with a guard rail (shaft, opening) is measured to that rail, never across the opening.
2. `Lanes`: which run carries the lane's length dim, on which side, `V1V3Same`.
3. `Walls`: finish face and outer face per side; `ClearAcross`, `ClearAlong`; `Landings` (depth to wall, clear).
4. `Expected`: every dim the rules need, with `Status` (`OK` / `missing` / `no CLEAR suffix` / `CLEAR is only for the clear width of a run` / `prefix should be …`) and the matching `DimId`.
5. `Tags`, `Spots`, `Paths`, `TreadNumbers`, `ProjectTypes`, `Notes`, `Issues`.

Show the user one table per view: run → state → treads × depth → length → clear width; then the issues grouped by rule (SA, SB, C, SD).

Stop and ask when:
- no host stairs, or the stairs are in a link;
- a side has no wall (open side, link walls) and the rule needs it;
- V1 and V3 differ (`V1V3Same:false`);
- treads × depth ≠ footprint length (`C:` issue) — report it, never edit the model;
- a category has no tag type used in the project.

## 2. Fix the text of existing dims

Existing dims that match but lack the text:

```
dims_text {viewId, mode:"preview", items:[{dimId, valueMm:1550, suffix:"CLEAR"}, {dimId, valueMm:3920, prefix:"280mm x 14T =", below:"(EQUAL TREADS)"}]}
dims_text {viewId, mode:"apply", items:[...], logPath}
```

- Prefix/suffix only. The value stays live. Never "Replace with text".
- Take the prefix from the audit's `Text`, the value from `Expected`.
- One spelling for the whole project: `280mm x 14T = 3920` (set prefix `280mm x 14T =` and suffix `CLEAR`: Revit adds the space itself). Rewrite variants such as `280x13T= `.

## 3. Path, tread numbers, run tags (deterministic)

```
stair_plan_annotate {viewId, mode:"preview"}
stair_plan_annotate {viewId, mode:"apply", logPath:"<review>/stair-annot-<viewId>.json"}
```

- `path` (SD): one Fixed Up Direction path per stairs, UP/DOWN text off. An existing path keeps its place; its type and text are fixed.
- `numbers` (C): tread numbers on each seen run without them (`numberSide`, default `left`). V1 gets the mirrored side, so the V1 and V3 columns never overlap in their shared lane.
- `runTags` (SB1): one tag per untagged run. By default (`runTagPlace:"outside"`) the head sits just outside the side wall of the run's lane (`runTagOffsetMm`, default 5 paper mm), with its text along the run and a leader into the seen part. The SA2 chain then goes outside these tags.
- Use `parts:[...]` to run only some of them. The command only adds what is missing, so re-running it on a half-done view is safe. Do **not** exclude a core stair to target one run: the seen / hidden test needs all of them; to give one stair another tag type, run `parts:["runTags"]` with the other stairs in `excludeStairIds` after the rest is placed.
- **Run tag type**: projects often have one stair run tag type per base level (it adds that level to the run's relative elevations). Preview `runTags` and read `Text`: `From EL … To EL …` must be the run's absolute elevations. Wrong → preview the next type; never guess from the type name. Record the stair → type pairs in the profile.
- **Tread number settings**: copy them from a finished view of the same core with `number_systems_copy {sourceId, targetIds}` (Display Rule, Number Size, Justify… copied as stored values). Never set a length parameter with `modify_element_parameter`: it parses the text as feet ("2" → 610 mm, the whole view turns black).
- **Riser numbering across stairs**: the start number is the Stairs parameter "Tread/Riser Start Number" (a model parameter: ask the user once for permission). Start = previous stair's start + its Actual Number of Risers, from the lowest stair of the core; list the whole chain (stairs id → base/top level → risers → start) in the profile.
- Stair paths: a Fixed Up path's `ShowUpText` property throws, the instance parameter "Show Up Text" works and is ON by default; the command turns it off (also on existing paths).

After apply, export the view image and check:
- the arrows point **up** (from the lower run to the higher);
- the numbers continue over the whole stair from the lowest riser of the building (sample: 1…15, 16…30, 31…45), with every second number shown. Each run starts at the previous run's last number + 1. A wrong start → fix the start number by hand, or ask;
- no number, tag or arrow sits on another.

Undo: `stair_plan_annotate {mode:"undo", logPath}`.

## 4. Dims SA1–SA4 (`dims_at_positions`)

Positions come from the audit (`Expected` FromMm / ToMm, view frame). Proven on a real view (2026-10-07):

```
dims_at_positions {viewId, mode:"preview", use3D:true, typeName:<check type>, refDims:[<a dim drawn by hand, if any>],
  dims:[{name:"SA2 bottom", measure:"right", lineMm:<line>, positions:[<grid>, {mm:<wall finish>, src:"3d"}, {mm:<run end>, id:<STAIRS id>, src:"view"}, {mm:<run start>, id:<STAIRS id>, src:"view"}, <grid>, {mm:<wall finish>, src:"3d"}]}]}
```

- Walls: 3D faces (`src:"3d"`), plan faces drift.
- Runs / landings: the plan lines of the **Stairs** element (`id:<stairs>, src:"view"`); faces of `StairsRun` / `StairsLanding` give dims that are not drawn.
- Handrails (SA1, SA3): dims the API builds on Top Rail lines are not drawn. **No hand dim is needed**:
  - `dims_rail_refs {viewId, mode:"probe" | "preview" | "apply", dims:[{measure, lineMm, positions:[{mm, rail:<top rail id>}, {mm, wall:<wall id>}, {mm, ref:"<stable>"}]}]}` builds each reference like a UI pick (`<top rail UniqueId>:1:INSTANCE:<symbol edge>:LINEAR`) and keeps only edges that give the right value and are drawn. Top rail ids: `dim_stable_refs {railingIds}`. If the automatic edge test rejects every edge (it happens on dims measured along the view Right), take the edge from `probe` and pass it as `{ref}`; a value of `-305` means that edge is not drawable.
  - A chain the user drew by hand in another view of the same railings: `dims_copy_refs {targetViewId, items:[{sourceDimId, typeName}]}` reuses its references (texts copied).
  - Rails above the cut plane are never dimensionable: use a rail below the cut at the same position.
- **SA3 end = the handrail end drawn on the view** (the rounded end of the centre handrail), not the first edge found and not a fixed "riser ± 80" (seen: −80 on one landing, +120 on the other). Probe the edges, export the view at 6000 px, crop the landing, pick the edge on that rounded end. The audit's `Clear` is a hint only.
- After apply: `view_elem_boxes {ids}` → a dim with `Box: null` is not drawn: delete it and change the reference.
- Then `dims_text` (`CLEAR`, `280mm x 16T =`), `dims_text_move` for short segments whose texts overlap (80 | 30 | 80).

Older method, still valid: a one-off dynamic command per view: preview → apply with `logPath` (created ids) → undo by deleting them (`dims_edit`). It must **reference real geometry**, never detail lines:


| What | Reference |
|---|---|
| Wall finish face | `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior / Exterior)`: take the face whose position equals the audit's `FinishFaceMm` / `OuterFaceMm` |
| Inner handrail edge | `railing.TopRail` / `GetHandRails()` / railing geometry with `Options{ComputeReferences=true, View=v}`: the vertical `PlanarFace` facing the run, at the audit's clear-width bound |
| First / last riser of a run, landing edge | run / landing geometry with `ComputeReferences`: vertical planar faces normal to travel, at the run's `A0` / `A1` |
| Door / window edges | `fi.GetReferences(FamilyInstanceReferenceType.Left / Right)` |
| Grid | `new Reference(grid)` (host grids only) |

Placement (as the sample sheet; spacing between dim lines 7 paper mm):

| Where | Content |
|---|---|
| Inside the core, across, next to both ends of the runs | SA1: `70 \| 1550 CLEAR \| 80 \| 300 \| 80 \| 1550 CLEAR \| 70`, plus the overall as a chain closed on the walls: `handrail \| overall \| handrail \| gap` (e.g. `80 \| 2590 \| 80 \| 390`), no CLEAR |
| Inside the core, along the well axis | SA3 chain closed on the finish wall: `gap \| handrail \| landing clear` (e.g. `49 \| 80 \| 1600`, `1393 \| 80 \| 2618`), no CLEAR, on the mid landing and on the floor landing |
| Outside each side wall | the SB1 run tags, then the SA2 chain `landing \| formula \| floor landing \| (grid) \| …`, then the overall (no CLEAR) |
| Outside the end walls | SA4: grid → wall faces → door/window edges → grid; overall |

A grid crossing the core goes into the SA2 chain. The audit accepts the split (`OK (split)`).

Then set the text with `dims_text`: `CLEAR` suffix **only on the clear width of each run**, formula prefixes on the run lengths. Re-run the audit: every `Expected` row must read `OK`.

## 5. Tags SB2–SB6, spots (`annot_place`)

`annot_place {viewId, mode, items:[{kind:"tag", elementId, typeName, right, up, leader, endRight, endUp}, {kind:"spot", elementId:<STAIRS id>, typeName, right, up}]}` — points in the view frame; read free space first with `view_elem_boxes {allAnnotations:true}` (ViewBox). Spots go on the **Stairs** element (its plan face at the point), as the project sheets do. A door the view does not draw gets no tag.

- Type: `ProjectTypes` from the audit (most used in the project). Never create a type.
- **Leaders are orthogonal** (user rule): one horizontal or vertical segment when the head lines up with the target, otherwise exactly one elbow (V+H or H+V); never diagonal. `annot_place` adds the elbow (`elbowFirst` V default / H); check with `tag_leaders_info` (shape `D` = diagonal → fix). Run tags: head straight above / below the seen part, vertical leader; keep the head box off wall notches and dims.
- SB2 railing: `IndependentTag.Create(..., addLeader:true, ...)`, head off the run lines (stair well, landing or beside the railing), short leader.
- SB2: `P01` on each wall handrail, `P02` on the centre railing (the project's codes). Keep the heads off the tread lines, even where the sample has them on the lines.
- SB3 spot elevation: `doc.Create.NewSpotElevation(view, topFaceRef, …)` on the mid landing, on the floor landing inside the core, and on the floor just outside the stair door (link floor → link reference). Not on the door swing or the arrow.
- SB4 doors/windows: exactly one tag each, close, off the dims.
- SB5 floor finish (`F13` in the sample) on the mid landing and the floor landing, next to their spot elevation.
- **SB6 wall finish is mandatory** (`W..`, one per wall finish kind seen, usually one per wall face): the mark the project already uses (often a Generic Annotation box, listed by the audit as `FinishMarks`). Code from the room's Wall Finish parameter, the finish legend, or the finished views of the same core; unknown → ask, never skip. Leader to the nearest wall face; heads may gather in a free inside corner of the core.

## 6. Verify and report

1. `stair_plan_audit` again → no `Issues` left except those the user accepted.
2. `annotation_overlaps {viewId}` → fix overlaps.
3. Export the **view** image (`export_sheet_images {sheetNumbers:[], viewIds:[id]}`) and look (`drafting-visual-check`): 3 runs, 3 run tags outside the walls with leaders, a spot on every seen landing, continuous numbers, a V-shaped arrow at the top end of each run, no UP/DOWN, dim lines aligned and in order, short texts (80 | 30 | 80, 49 | 80) apart, no dim line through a CLEAR text or a tag. `annotation_overlaps` does not see generic-annotation finish marks or path texts: look at the image. Compare with the views already finished on the same sheet.
4. Report per `drafting-session`: exact view names; table per view of what was added / fixed; **Cần xem** (V1 ≠ V3, measured-to-run-edge widths, tags moved by hand); **Việc tồn** (model mismatches, link stairs/walls, dims still in the check type).

## Never

- Count treads by eye or from the visible lines.
- Replace a dim value with text (use prefix/suffix).
- Use a tag, spot or path type the project does not already have without asking.
- Show UP/DOWN text on a stair path.
- Put a run tag under a dim, or a railing tag head on the run lines.
- Mix formula spellings (`280mm x 14T = 3920` only).
- Count the top tread that sits at the landing level.
- Put `CLEAR` on anything but the clear width of a run (not on landings, overalls, handrails, walls).
- Measure a clear width across an opening (shaft) or to a wall when a railing / step edge comes first.
- Skip an item of the standard silently (SB6!). Done means ĐỦ – ĐÚNG – ĐẸP (`drafting-session`).
- Dim the same length on both sides of the core.
- Edit stairs, railings, walls or floors (R1). Exception, with the user's permission: the Stairs "Tread/Riser Start Number" for continuous numbering.
- Set a length parameter with `modify_element_parameter` (unit parsing: feet).
- Draw a diagonal leader.
- Take the SA3 end from the audit without checking the drawn handrail end on the image.
- Annotate with fewer runs / landings than the view shows.
