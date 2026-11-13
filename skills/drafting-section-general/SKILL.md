---
name: drafting-section-general
description: "General building sections (mặt cắt chung, usually 1:200) via Revit MCP, card by card: structural fill (SF1–SF10) over cut concrete, earth region underground. Use for mặt cắt chung, fill / tô kết cấu, tô dầm sàn móng bị cắt, filled region kết cấu, structural fill."
---

# General sections (khai triển mặt cắt chung)

**Status: started.** Cards so far: structural fill; new cards are added when their rules are decided with the user.

**Tools** (load all in one ToolSearch call, when a card needs them): `export_sheet_images`, `structural_cut_fill`, `ui_view_focus`.

Standard: `~/.claude/drafting-domain/drafting-section-general.md` (Vietnamese).

Load `drafting-session` first. Take these values from the project `drafting-profile.md` (`templates/drafting-profile.md`), row "Fill kết cấu":
- the structural models (`sources`);
- the filled region types for concrete and earth (a model without an earth type gets one on the fill pattern `Earth` Revit ships with);
- the boundary line style;
- the ground floor level and the name patterns of pile caps and of piles;
- the mark type suffix.

Scope: general building section views on the sheets of the job, usually at 1:200. Not in scope:
- elevations (nothing is cut): their own topics;
- stair views: `drafting-stair-section-parallel` / `drafting-stair-section-cross`;
- door and window dims and tags on the same views: `drafting-opening-dims`, `drafting-opening-tags`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-structural-fill.md` | Structural fill (fill kết cấu bị cắt) |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

## Never

- Edit structural elements, floors or links (R1): only filled regions on the view.
- Touch filled regions of other drafters or of other types (earth, finishes, masks).
