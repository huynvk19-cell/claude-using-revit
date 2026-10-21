---
name: drafting-ceiling-plan
description: "Develop reflected ceiling plans (khai triển mặt bằng trần) via Revit MCP, one step card at a time. Cards: tag room (RT1–RT4: type chosen by the user, add-on = neighbours' type; never across walls / ceiling edges; at the room's diagonal centre, inside if it fits, else outside with a leader). Use for khai triển mặt bằng trần, mặt bằng trần, RCP, ceiling plan, tag room trên mặt bằng trần."
---

# Reflected ceiling plan (khai triển mặt bằng trần)

**Tools** (load all in one ToolSearch call): `annot_place`, `delete_element`, `export_sheet_images`, `mark_types`, `room_tags_broken`, `room_tags_missing`, `room_tags_score`, `roomtag_place_ortho`, `tag_leaders_info`, `ui_view_focus`.

Standard: `~/.claude/drafting-domain/drafting-ceiling-plan.md` (Vietnamese); room tag rules RT1–RT4 in `drafting-annotation.md` §2.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the room tag type, when the project fixes one → `typeName` (otherwise the user chooses, RT1);
- the room tag size on paper → `tagSizeMm`.

Scope: ceiling plan views. Floor plans → `drafting-plan-dims`; grid dims → `drafting-grid-dims`; fixing tags outside their room or showing "?" → `drafting-tags-titles`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-tag-room.md` | Tag room (tag phòng) |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

## Never

- Edit a room, wall or ceiling (R1).
- Choose the room tag type yourself on a new job (RT1).
- Move, retype or delete room tags placed by other drafters.
