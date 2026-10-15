---
name: drafting-plan-dims
description: "Develop floor plan dimension drawings (khai triển mặt bằng kích thước) via Revit MCP, one step card at a time. Cards: tag room (RT1–RT4: type chosen by the user, add-on = neighbours' type; never across walls / floor edges; at the room's diagonal centre, inside if it fits, else outside with a leader). Use for khai triển mặt bằng kích thước, mặt bằng kích thước, tag room, tag phòng còn thiếu, đặt tag phòng, floor plan dims."
---

# Floor plan dimensions (khai triển mặt bằng kích thước)

**Tools** (load all in one ToolSearch call): `annot_place`, `delete_element`, `export_sheet_images`, `mark_types`, `room_tags_broken`, `room_tags_missing`, `tag_leaders_info`, `ui_view_focus`.

Standard: `~/.claude/drafting-domain/drafting-plan-dims.md` (Vietnamese); room tag rules RT1–RT5 in `drafting-annotation.md` §2.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the room tag type, when the project fixes one → `typeName` (otherwise the user chooses, RT1);
- the room tag size on paper → `tagSizeMm`.

Scope: floor plan views. Stair core plans → `drafting-stair-plan`; ceiling plans → `drafting-ceiling-plan`; grid dims → `drafting-grid-dims`; fixing tags outside their room or showing "?" → `drafting-tags-titles`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-tag-room.md` | Tag room (tag phòng) |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

## Never

- Edit a room, wall or floor (R1).
- Choose the room tag type yourself on a new job (RT1).
- Move, retype or delete room tags placed by other drafters.
