---
name: drafting-ceiling-plan
description: "Develop reflected ceiling plans (khai triển mặt bằng trần) via Revit MCP, one step card at a time. Cards: tag room (TR1–TR8: shortest leader, inside the room when it fits; scored 0–10, 0 on walls / ceiling edges, on or along leaders and walls, −3 per room crossed, card 0 if a room is untagged). Use for khai triển mặt bằng trần, mặt bằng trần, RCP, ceiling plan, tag room trên mặt bằng trần."
---

# Reflected ceiling plan (khai triển mặt bằng trần)

**Tools** (load all in one ToolSearch call): `delete_element`, `export_sheet_images`, `mark_types`, `room_tags_missing`, `room_tags_score`, `roomtag_place_ortho`, `ui_view_focus`.

Standard: `~/.claude/drafting-domain/drafting-ceiling-plan.md` (Vietnamese); room tag rules TR1–TR8 in `drafting-annotation.md` §2.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the room tag type, when the project fixes one → `typeName` (otherwise ask the user);
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
- Choose the room tag type yourself on a new job.
- Move, retype or delete room tags placed by other drafters.
