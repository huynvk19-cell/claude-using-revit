---
name: drafting-tags-titles
description: "Room tags (outside room, showing ?), and viewport titles on sheets via Revit MCP. Use for room tag, tên phòng hiện dấu hỏi, title view, căn giữa title, viewport title."
---

# Tags and titles

**Tools** (load all in one ToolSearch call): `room_tags_broken`, `room_tags_outside`, `viewport_title_audit`, `viewport_titles_place`.

Standards: `~/.claude/drafting-domain/drafting-annotation.md`.

## Door/window tags

Use the `drafting-opening-tags` skill (rules T1-T8).

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-room-tags.md` | Room tags (tag phòng) |
| 01 | `steps/01-viewport-titles.md` | Viewport titles (title view) |

A card says: what to do (commands), **done when**, **skip only if**, never.

