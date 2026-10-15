# Dims: shared notes (cards 02–05)

Read once per job, before the first dim card.

## Existing dims first

Existing dims that match the item but lack the text: fix them before adding anything.

```
dims_text {viewId, mode:"preview", items:[{dimId, valueMm:1550, suffix:"CLEAR"}, {dimId, valueMm:3920, prefix:"280mm x 14T =", below:"(EQUAL TREADS)"}]}
dims_text {viewId, mode:"apply", items:[...], logPath}
```

- Prefix / suffix only: the value stays live. Never "Replace with text" (R9).
- Prefix from the audit's `Text`, value from `Expected`. One spelling: `280mm x 14T = 3920` (Revit adds the space after `=` itself).

## New dims

- Positions come from the audit (`Expected` FromMm / ToMm, view frame). Dim type: the **check** type (profile).
- Walls: 3D faces (`src:"3d"`); plan faces drift.
- Runs / landings: the plan lines of the **Stairs** element (`id:<stairs>, src:"view"`); faces of `StairsRun` / `StairsLanding` give dims that are not drawn.
- After apply: `view_elem_boxes {ids}` → a dim with `Box: null` is not drawn: delete it and change the reference.
- `dims_text_move` for short segments whose texts overlap.
- Dim lines 7 paper mm apart, in the order of the standard's layout table.

## Older method, still valid

A one-off dynamic command per view: preview → apply with `logPath` (created ids) → undo by deleting them (`dims_edit`). It must **reference real geometry**, never detail lines:

| What | Reference |
|---|---|
| Wall finish face | `HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior / Exterior)`: the face at the audit's `FinishFaceMm` / `OuterFaceMm` |
| Inner handrail edge | `railing.TopRail` / `GetHandRails()` / railing geometry with `Options{ComputeReferences=true, View=v}`: the vertical `PlanarFace` facing the run |
| First / last riser of a run, landing edge | run / landing geometry with `ComputeReferences`: vertical planar faces normal to travel |
| Door / window edges | `fi.GetReferences(FamilyInstanceReferenceType.Left / Right)` |
| Grid | `new Reference(grid)` (host grids only) |
