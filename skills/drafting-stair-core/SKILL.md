---
name: drafting-stair-core
description: "Detail a stair core PLAN via Revit MCP, rules SA1–SD: clear-width dims with suffix CLEAR (runs between inner handrail edges, wall to wall, landings), run-length dims with the formula '280mm x 14T = 3920' plus the landing to the wall, wall/door/window chains to the grids in an outer layer; tags on the 3 seen runs (V1 half beyond the cut, V2 full, V3 half before the cut), railings, spot elevations (landing, floor outside the stair door), doors/windows, landing and wall finishes; tread counts per run from the model; stair path with an arrow only (no UP/DOWN). Use for lõi thang, thang bộ, mặt bằng thang, chi tiết thang, dim thang, thông thuỷ, CLEAR, chiếu nghỉ, vế thang, số bậc, đếm bậc, tag thang, tay vịn, lan can, stair path, mũi tên thang, stair core plan."
---

# Stair core plan (SA1–SD)

Standard: `~/.claude/drafting-domain/drafting-stair-core.md` (Vietnamese). Its rules decide every case. Read it first.

Load `drafting-session` first. From the project `drafting-profile.md` take:
- the **check** dim type → new dims;
- stair core views (name filter) and whether tread numbers are shown (default: yes);
- tag / path type overrides, if the profile names any. Otherwise the project's most-used type per category.

Plan views only. **One view per call.** Tools have no project defaults.

## 1. Audit (read-only)

```
stair_core_audit {viewId, outPath:"<review>/stair-<viewId>.json"}
```

Read, in this order:
1. `Runs`: label **V1 / V2 / V3** (beyond cut / full / cut), treads, tread depth, `Text` (e.g. `280mm x 14T = 3920`), `ClearWidth` and what bounds it (`ClearFrom` / `ClearTo`). These numbers come from the model: **never count treads from the drawing**.
2. `Lanes`: which run carries the lane's length dim, on which side, `V1V3Same`.
3. `Walls`: finish face and outer face per side; `ClearAcross`, `ClearAlong`; `Landings` (depth to wall, clear).
4. `Expected`: every dim the rules need, with `Status` (`OK` / `missing` / `no CLEAR suffix` / `prefix should be …`) and the matching `DimId`.
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
dims_text {viewId, mode:"preview", items:[{dimId, valueMm:1200, suffix:" CLEAR"}, {dimId, valueMm:3920, prefix:"280mm x 14T = "}]}
dims_text {viewId, mode:"apply", items:[...], logPath}
```

- Prefix/suffix only. The value stays live. Never "Replace with text".
- Take the prefix from the audit's `Text`, the value from `Expected`.

## 3. Path, tread numbers, run tags (deterministic)

```
stair_core_annotate {viewId, mode:"preview"}
stair_core_annotate {viewId, mode:"apply", logPath:"<review>/stair-annot-<viewId>.json"}
```

- `path` (SD): one Fixed Up Direction path per stairs, UP/DOWN text off. An existing path keeps its place; its type and text are fixed.
- `numbers` (C): tread numbers on each seen run without them (`numberSide`, default `left`).
- `runTags` (SB1): one tag inside the seen part of each untagged run, on the side away from the numbers, no leader.
- Use `parts:[...]` to run only some of them.

After apply, export the view image and check:
- the arrows point **up** (from the lower run to the higher);
- each run's numbers run 1…n and the last number equals the `nT` of its formula; a half run continues its own numbering (e.g. V1 shows 8…14). Wrong start → set the tread number's start value by hand or ask;
- no number, tag or arrow sits on another.

Undo: `stair_core_annotate {mode:"undo", logPath}`.

## 4. Dims SA1–SA4 (no batch tool yet)

Write a one-off dynamic command per view: preview → apply with `logPath` (created ids) → undo by deleting them (`dims_edit`). It must **reference real geometry**, never detail lines:

| What | Reference |
|---|---|
| Wall finish face | `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior / Exterior)`: take the face whose position equals the audit's `FinishFaceMm` / `OuterFaceMm` |
| Inner handrail edge | `railing.TopRail` / `GetHandRails()` / railing geometry with `Options{ComputeReferences=true, View=v}`: the vertical `PlanarFace` facing the run, at the audit's clear-width bound |
| First / last riser of a run, landing edge | run / landing geometry with `ComputeReferences`: vertical planar faces normal to travel, at the run's `A0` / `A1` |
| Door / window edges | `fi.GetReferences(FamilyInstanceReferenceType.Left / Right)` |
| Grid | `new Reference(grid)` (host grids only) |

Placement (paper mm × view scale, from the **outer** wall face, outward):

| Line | Distance | Content |
|---|---|---|
| Layer 1, line 1 | 7 | SA1 chain (outside an end wall, default: the landing end) or SA2 chain (outside each side wall, one lane per side) |
| Layer 1, line 2 | 14 | Overall wall-to-wall, suffix ` CLEAR` |
| Layer 2 | 21 | SA4: grid → wall faces → door/window edges → grid |
| Inside | on the landing | SA3 landing clear, suffix ` CLEAR` |

Then set the text with `dims_text` (CLEAR suffixes, formula prefixes). Re-run the audit: every `Expected` row must read `OK`.

## 5. Tags SB2–SB6 (one-off command or by hand)

- Type: `ProjectTypes` from the audit (most used in the project). Never create a type.
- SB2 railing: `IndependentTag.Create(..., addLeader:true, ...)`, head off the run lines (stair well, landing or beside the railing), short leader.
- SB3 spot elevation: `doc.Create.NewSpotElevation(view, topFaceRef, …)` on the landing's top face and on the floor just outside the stair door (link floor → link reference). Not on the door swing or the arrow.
- SB4 doors/windows: exactly one tag each, close, off the dims.
- SB5 landing finish, SB6 wall finish: the tag kind the project already uses (material tag, keynote, finish tag); SB6 leader to the nearest wall face, heads may gather in a free inside corner of the core.

## 6. Verify and report

1. `stair_core_audit` again → no `Issues` left except those the user accepted.
2. `annotation_overlaps {viewId}` → fix overlaps.
3. Export the sheet image and look (`drafting-visual-check`): 3 runs, 3 run tags, numbers per run, arrow up, no UP/DOWN, dim layers aligned and in order.
4. Report per `drafting-session`: exact view names; table per view of what was added / fixed; **Cần xem** (V1 ≠ V3, measured-to-run-edge widths, tags moved by hand); **Việc tồn** (model mismatches, link stairs/walls, dims still in the check type).

## Never

- Count treads by eye or from the visible lines.
- Replace a dim value with text (use prefix/suffix).
- Use a tag, spot or path type the project does not already have without asking.
- Show UP/DOWN text on a stair path.
- Put a run tag under a dim, or a railing tag head on the run lines.
- Dim the same length on both sides of the core.
- Edit stairs, railings, walls or floors (R1).
