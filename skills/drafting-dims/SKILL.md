---
name: drafting-dims
description: "Add or fix dimensions on Revit drawings via Revit MCP: grid dims (chain + overall), level dims on sections, door/window/roll-up door dims (vertical + horizontal) on elevations and sections, dim type swap. Use for dim trục, dim cao độ, dim cửa, dim cửa cuốn, dim lỗi, đổi màu dim, grid dimensions, opening dimensions."
---

# Dimensions

Standards and tools:
- `~/.claude/drafting-domain/drafting-dimensions.md`
- `~/.claude/drafting-domain/drafting-tools.md`
- the project profile (dim types, grid sides, grids that coincide, excluded views)

## Grid dims (plans)

1. Audit with `grid_dims_audit` / `grid_dim_rows`. Skip the excluded views: stairs, toilet/utility, narrow partial strips, and any others the profile lists.
2. Dependent views: their dims live in the parent view.
   - Run `grid_dims_layout` on the parent.
   - Place each dim inside each dependent's crop.
   - Use `hideInViews` for the other dependent.
3. Put dims on the sides the profile gives. Use one side when space is tight.
4. Dims that reference link grids: delete them and redo them on host grids.

## Sections / elevations (ONE view per call)

1. Grid dims on the bubble side, plus `level_dims_add` (chain + overall).
2. Openings, vertical:
   ```
   elevation_opening_dims {mode:"apply", strictVisibility:true, verticalOnly:true,
                           moveExisting:false, gridNearDist:12000,
                           nominalFamilies:["ROLL UP"], logPath}
   ```
   Then run `dedupe_new_vdims {fromLog}`.
3. Openings, horizontal: the same call with `horizontalOnly:true`.
   - Do BOTH directions unless the user limits it.
   - "no free line for row N" → report that row as manual.
4. Roll-up doors: nominal size only (named references TOP / LEFT / RIGHT).
   - Check with `rollup_dims_check`.
   - Fix with `rollup_dims_add` (`fixChains`).
5. A chain that bridges far-apart openings (segment over ~50 m) → `dims_split {maxMm:50000}`.
6. Check the result for:
   - values like 50 / 2135, a sign that a hidden door was dimensioned;
   - duplicates of existing dims;
   - overlapping text.

## Type

Work in the profile's check type. Swap to the official type with `swap_dim_type` only after the user approves.
