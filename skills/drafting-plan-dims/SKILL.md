---
name: drafting-plan-dims
description: "Develop floor plan dimension drawings (khai triển mặt bằng kích thước) via Revit MCP, one step card at a time. Cards: door/window dims to walls and grids (PD1–PD6), door/window tags (PT1–PT6), tag room (RT1–RT4), floor spot elevations (FL1–FL3), wall tags (WT1–WT5), slope marks (SL1–SL5). Use for khai triển mặt bằng kích thước, mặt bằng kích thước, dim cửa trên mặt bằng, dim cửa tới trục, dim cửa tới tường, tag cửa mặt bằng, tag room, tag phòng còn thiếu, tag cao độ sàn, tag tường, wall tag, ký hiệu độ dốc, spot slope, floor plan dims."
---

# Floor plan dimensions (khai triển mặt bằng kích thước)

**Tools** (load all in one ToolSearch call): `annot_owners`, `annot_place`, `annotation_overlaps`, `delete_element`, `dims_at_positions`, `dims_edit`, `dims_text_move`, `door_window_tag_check`, `export_sheet_images`, `floor_levels_probe`, `list_view_dims`, `mark_types`, `query_elements_with_filter`, `room_at_point`, `room_spots_missing`, `room_tags_broken`, `room_tags_missing`, `roomtag_place_ortho`, `rooms_dump`, `slope_spots_place`, `stair_door_spots`, `tag_leaders_info`, `tag_reposition`, `ui_view_focus`, `view_elem_boxes`.

Standard: `~/.claude/drafting-domain/drafting-plan-dims.md` (Vietnamese): PD1–PD6 door/window dims, PT1–PT6 door/window tags, WT1–WT5 wall tags; room tag rules RT1–RT4 in `drafting-annotation.md` §2, floor spot rules FL1–FL3 in §2b, slope mark rules SL1–SL5 in §2c.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the check dim type → `typeName` of `dims_at_positions` (card 00);
- the roll-up door family (nominal `LEFT` / `RIGHT`) (card 00);
- the user's Revit account: own annotations = LastChangedBy this user (`annot_owners`, cards 00–01);
- the plan areas kept to grid dims only (no door dims / tags there) (cards 00–01);
- the room tag type, when the project fixes one → `typeName` (otherwise the user chooses, RT1), and the room tag size on paper → `tagSizeMm` (card 02);
- the spot elevation type (FL3), the small-room area threshold `minAreaM2`, the stair room / stair view name parts and the ground-exit symbol `civilSymbolId` (card 03);
- the wall tag type, when the project fixes one (otherwise the type already used, else ask, WT1), and the toilet room name parts (default `TOILET`, `WC`, WT4) (card 04);
- the spot slope type, when the project fixes one (otherwise an existing type with Direction Down and decimal / percentage units, SL2–SL4) (card 05).

Scope: floor plan views. Stair core plans → `drafting-stair-plan`; ceiling plans → `drafting-ceiling-plan`; grid dims → `drafting-grid-dims`; door/window dims and tags on elevations / sections → `drafting-opening-dims` / `drafting-opening-tags`; fixing room tags outside their room or showing "?" → `drafting-tags-titles`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card. The order is fixed: dims first, then the tags that must keep clear of them, then the floor spots that keep clear of the room tags, then the wall tags and the slope marks that keep clear of everything placed before.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-opening-dims.md` | Door / window dims to walls and grids (dim cửa tới tường, trục) |
| 01 | `steps/01-opening-tags.md` | Door / window tags (tag cửa) |
| 02 | `steps/02-tag-room.md` | Tag room (tag phòng) |
| 03 | `steps/03-floor-spots.md` | Floor spot elevations (tag cao độ sàn) |
| 04 | `steps/04-wall-tags.md` | Wall tags (tag tường) |
| 05 | `steps/05-slope-marks.md` | Slope marks (ký hiệu độ dốc) |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

First job on a project with no plan sample from the user: run cards 00–01 on **one** view, let the user review it, write what they change into the profile, then do the rest.

## Never

- Edit a room, wall, door, window, floor or grid (R1).
- Dim or tag a hidden or linked opening; dim or tag on OVERALL / key plans.
- Choose the room tag type yourself on a new job (RT1); load or make a wall tag type (no wall tag type in the project → ask, WT1).
- Move, retype or delete tags or dims placed by other drafters (check with `annot_owners`), unless the user allows it this session.
