---
name: drafting-tags-titles
description: "Room tags (outside room, showing ?), and viewport titles on sheets via Revit MCP. Use for room tag, tên phòng hiện dấu hỏi, title view, căn giữa title, viewport title."
---

# Tags and titles

Standards: `~/.claude/drafting-domain/drafting-annotation.md`.

## Door/window tags

Use the `drafting-opening-tags` skill (rules T1-T8).

## Room tags

- **Outside its room** → `room_tags_outside` apply. It moves the tag to the room point and turns the leader off.
- **Shows "?"** → `room_tags_broken` apply. It colours the tag red.
  - Report the room as a model-side fix.
  - Never edit the room.

## Viewport titles

1. Audit with `viewport_title_audit`, using the sheet prefix.
2. Place with `viewport_titles_place`.
   - The title is centred under the drawing, just below the lowest content.
   - Use `ignoreCrop` when the crop is far from the drawing.
3. Re-run after any grid, dim or crop change on that sheet.
4. Leave stair-view titles alone unless a comment asks.
