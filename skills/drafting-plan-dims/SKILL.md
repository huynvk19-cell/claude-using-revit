---
name: drafting-plan-dims
description: "Develop floor plan dimension drawings (khai triển mặt bằng kích thước) via Revit MCP, one step card at a time. Cards: door/window dims to walls and grids (PD1–PD6), door/window tags (PT1–PT6), tag room (RT1–RT4). Use for khai triển mặt bằng kích thước, mặt bằng kích thước, dim cửa trên mặt bằng, dim cửa tới trục, dim cửa tới tường, tag cửa mặt bằng, tag room, tag phòng còn thiếu, floor plan dims."
---

# Floor plan dimensions (khai triển mặt bằng kích thước)

**Tools** (load all in one ToolSearch call): `annot_owners`, `annot_place`, `annotation_overlaps`, `delete_element`, `dims_at_positions`, `dims_edit`, `dims_text_move`, `door_window_tag_check`, `export_sheet_images`, `list_view_dims`, `mark_types`, `query_elements_with_filter`, `room_tags_broken`, `room_tags_missing`, `tag_leaders_info`, `tag_reposition`, `ui_view_focus`, `view_elem_boxes`.

Standard: `~/.claude/drafting-domain/drafting-plan-dims.md` (Vietnamese): PD1–PD6 door/window dims, PT1–PT6 door/window tags; room tag rules RT1–RT4 in `drafting-annotation.md` §2.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the check dim type → `typeName` of `dims_at_positions` (card 00);
- the roll-up door family (nominal `LEFT` / `RIGHT`) (card 00);
- the user's Revit account: own annotations = LastChangedBy this user (`annot_owners`, cards 00–01);
- the plan areas kept to grid dims only (no door dims / tags there) (cards 00–01);
- the room tag type, when the project fixes one → `typeName` (otherwise the user chooses, RT1), and the room tag size on paper → `tagSizeMm` (card 02).

Scope: floor plan views. Stair core plans → `drafting-stair-plan`; ceiling plans → `drafting-ceiling-plan`; grid dims → `drafting-grid-dims`; door/window dims and tags on elevations / sections → `drafting-opening-dims` / `drafting-opening-tags`; fixing room tags outside their room or showing "?" → `drafting-tags-titles`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card. The order is fixed: dims first, then the tags that must keep clear of them.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-opening-dims.md` | Door / window dims to walls and grids (dim cửa tới tường, trục) |
| 01 | `steps/01-opening-tags.md` | Door / window tags (tag cửa) |
| 02 | `steps/02-tag-room.md` | Tag room (tag phòng) |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

First job on a project with no plan sample from the user: run cards 00–01 on **one** view, let the user review it, write what they change into the profile, then do the rest.

## Never

- Edit a room, wall, door, window, floor or grid (R1).
- Dim or tag a hidden or linked opening; dim or tag on OVERALL / key plans.
- Choose the room tag type yourself on a new job (RT1).
- Move, retype or delete tags or dims placed by other drafters (check with `annot_owners`), unless the user allows it this session.
