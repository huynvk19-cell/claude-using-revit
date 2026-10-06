---
name: drafting-dims
description: "Add or fix dimensions on Revit drawings via Revit MCP: grid dims (chain + overall), level dims on sections, dim type swap (door/window dims → drafting-opening-dims). Use for dim trục, dim cao độ, dim lỗi, đổi màu dim, grid dimensions, level dimensions."
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

1. Grid dims on the bubble side, plus `level_dims_add {viewId, typeName:<profile check type>}` (chain + overall).
2. Windows and doors (vertical + horizontal): use the `drafting-opening-dims` skill.

## Type

Work in the profile's check type. Swap to the official type with `swap_dim_type` only after the user approves.
