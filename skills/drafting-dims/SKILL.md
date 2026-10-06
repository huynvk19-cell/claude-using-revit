---
name: drafting-dims
description: "Add or fix dimensions on Revit drawings via Revit MCP: level dims on sections/elevations, dim type swap; routes grid dims to drafting-grid-dims and door/window dims to drafting-opening-dims. Use for dim cao độ, dim lỗi, đổi màu dim, level dimensions."
---

# Dimensions

Standards and tools:
- `~/.claude/drafting-domain/drafting-dimensions.md`
- `~/.claude/drafting-domain/drafting-tools.md`
- the project profile (dim types, excluded views)

## Grid dims (plans, elevations, sections)

Use the `drafting-grid-dims` skill (rules G1–G4, tool `grid_dims_band`). The older `grid_dims_add` / `grid_dims_layout` place dims on both sides and inside the crop, which breaks G2/G3. Use them only when the user asks for that layout.

## Sections / elevations (ONE view per call)

1. Level dims: `level_dims_add {viewId, typeName:<profile check type>}` (chain + overall).
2. Windows and doors (vertical + horizontal): use the `drafting-opening-dims` skill.

## Type

Work in the profile's check type. Swap to the official type with `swap_dim_type` only after the user approves.
