---
name: drafting-dims
description: "Add or fix dimensions on Revit drawings via Revit MCP: level dims on sections/elevations, dim type swap; routes grid dims to drafting-grid-dims and door/window dims to drafting-opening-dims. Use for dim cao độ, dim lỗi, đổi màu dim, level dimensions."
---

# Dimensions

**Tools** (load all in one ToolSearch call): `grid_dims_add`, `grid_dims_band`, `grid_dims_layout`, `level_dims_add`, `mark_types`.

Standards and tools:
- `~/.claude/drafting-domain/drafting-dimensions.md`
- `~/.claude/drafting-domain/drafting-tools.md`: search it for the command you need, do not read it whole
- the project profile (dim types, excluded views)

## Grid dims (plans, elevations, sections)

Use the `drafting-grid-dims` skill (rules G1–G4, tool `grid_dims_band`). The older `grid_dims_add` / `grid_dims_layout` place dims on both sides and inside the crop, which breaks G2/G3. Use them only when the user asks for that layout.

## Sections / elevations (ONE view per call)

1. Level dims: `level_dims_add {viewId, typeName:<profile check type>}` (chain + overall).
   - **Done when**: every level shown has its chain + overall, in the check type, on one side. **Skip only if**: the view already has them.
2. Windows and doors (vertical + horizontal): use the `drafting-opening-dims` skill (on floor plans: `drafting-plan-dims` card 00).

## Type

Work in the check type (the official dim type + the profile mark suffix; `mark_types {action:"ensure"}` creates it). Back to the official type only after the user approves: `mark_types {action:"restore", suffix, mode:"preview"}` → `apply`, or the user does Select All Instances by hand.
