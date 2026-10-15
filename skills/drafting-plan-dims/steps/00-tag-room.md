# 00 · Tag room (tag phòng)

**Skill** `drafting-plan-dims` · **Standard** `drafting-annotation.md` §2 RT1–RT4 · **Tools** `room_tags_missing`, `annot_place`, `room_tags_broken`, `delete_element`, `mark_types`, `export_sheet_images`
**Applies to** every floor plan view of the job.

## Do

1. **Type (RT1).** New job → ask the user once which room tag type (list the types already on the sheet). Add-on request ("tag các room còn thiếu") → leave `typeName` empty: the command takes the type of the nearest tagged rooms (`NeighbourType`).
2. **Find and place (RT2–RT4).** Plans may go many per call:
   ```
   room_tags_missing {viewIds:[batch], typeName, tagSizeMm}
   ```
   Per view: `Untagged`, `Inside` (no leader), `Outside` (leader), `Problems`, `Items`. No `tagSizeMm` in the profile → the command uses the box of a placed tag (wider than the text); say so in the report.
3. Per view, open it (`ui_view_focus {action:"open", viewId}`), then
   ```
   annot_place {mode:"preview", viewId, items:<Items>}
   annot_place {mode:"apply",   viewId, items:<Items>, logPath:"<review>/room-tags-<viewId>.json"}
   ```
4. `room_tags_broken` on the views: a new tag with a leader showing "?" (narrow room) → delete that tag (`delete_element`) and place it inside without a leader (`annot_place`, `leader:false`), or list it.
5. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_tags_missing` again: `Untagged` = 0 on every view, or each room left is in `Problems` with its reason (→ Việc tồn).
- On the image: no tag crosses a wall, a floor edge, a dim, a tag or a text; leaders only vertical / horizontal; each tag or leader dot sits near the room's diagonal centre.

## Skip only if

The view shows no rooms (`Rooms` = 0), or every room is already tagged (`Untagged` = 0).

## Never

- A tag head across a wall or a floor edge (RT2); a diagonal leader.
- Pick the type yourself on a new job (RT1).
- Edit a room; a room with no name / number stays "?" and is reported (R1).
