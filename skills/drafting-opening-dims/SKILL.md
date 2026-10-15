---
name: drafting-opening-dims
description: "Dimension every visible window and door (incl. roll-up doors) on elevation and section views, ONE OPENING AT A TIME: vertical chain host level → sill → head, horizontal chain grid → edge → edge → grid, placed right next to the opening, referencing the opening itself; then tidy overlaps. Use for dim cửa sổ, dim cửa đi, dim cửa cuốn, dim chiều cao cửa, dim khoảng cách cửa, dim mặt đứng, dim mặt cắt, cửa thiếu dim, opening dimensions."
---

# Opening dims (elevations / sections)

**Tools** (load all in one ToolSearch call): `annotation_overlaps`, `dims_declutter`, `dims_edit`, `export_sheet_images`, `highlight_elements`, `opening_dims_each`.

Standard: `~/.claude/drafting-domain/drafting-opening-dims.md`. Rules Q1–Q5 decide every case. Read it first.

Project values (check dim type, roll-up family name) come from the project `drafting-profile.md`. The tools have no project defaults: a missing type name returns an error.

The user requires the work to go **opening by opening**. A row/batch tool that counts a dim as "existing" by position leaves openings without dims. So never trust a batch result: audit by reference.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-audit.md` | Audit (khảo sát dim cửa) |
| 01 | `steps/01-dim-each-opening.md` | Dim each opening (dim từng cửa) |
| 02 | `steps/02-tidy.md` | Tidy (dọn dim cửa) |
| 03 | `steps/03-recheck.md` | Recheck (kiểm lại) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Dimension a hidden opening.
- Draw a vertical dim across its own opening.
- Push a dim into another storey.
- Reference a link.
- Batch several views in one call.
