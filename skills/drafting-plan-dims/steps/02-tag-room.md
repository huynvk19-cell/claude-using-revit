# 02 · Tag room (tag phòng)

**Skill** `drafting-plan-dims` · **Standard** `drafting-annotation.md` §2 RT1–RT4 · **Tools** `room_tags_missing`, `roomtag_place_ortho`, `room_tags_broken`, `tag_leaders_info`, `delete_element`, `mark_types`, `export_sheet_images`
**Applies to** every floor plan view of the job.

## Do

1. **Type (RT1).** New job → ask the user once which room tag type (list the types already on the sheet). Add-on request ("tag các room còn thiếu") → leave `typeName` empty: the command takes the type of the nearest tagged rooms (`NeighbourType`).
2. **Find and place (RT2–RT4).** Plans may go many per call:
   ```
   room_tags_missing {viewIds:[one view], typeName, tagSizeMm, outPath:"<review>/p1-<viewId>.json"}
   ```
   Per view: `Untagged`, `Inside` (no leader), `Outside` (leader); with `outPath` the `Items` + `Problems` go to the file (one view per call, short result). Kept clear: walls, floor / ceiling edges, dims, tags, texts, grid bubbles (`bubbleMm`, default 10), other room tag leaders; a leader never runs through a tag or text. Dense views: run twice, `maxLeaderMm` 30 then 70, placing between runs. No `tagSizeMm` in the profile → the command uses the box of a placed tag (wider than the text); say so in the report.
3. Per view, open it (`ui_view_focus {action:"open", viewId}`), then place: the positions are the tag BODY centre and `roomtag_place_ortho` centres the body there (a room tag head is not its body centre; `annot_place` would shift the tag and bend the leader). Remove rooms that must not be tagged (e.g. rooms of another level that the detail sheets of the same level do not tag) from the file first. Undo: `roomtag_place_ortho {mode:"undo", logPath}`.
   ```
   roomtag_place_ortho {mode:"apply", viewId, itemsPath:"<review>/p1-<viewId>.json", logPath:"<review>/place-p1-<viewId>.json", brief:true}
   ```
4. `room_tags_broken` on the views: a new tag with a leader showing "?" (narrow room) → delete that tag (`delete_element`) and place it inside without a leader (`roomtag_place_ortho`, no `ex/ey`), or list it.
5. Leaders (all tags, `drafting-annotation.md` "Leader của tag"): straight, one right-angle elbow when needed, horizontal / vertical on the paper. `tag_leaders_info {viewId}` per view: every room tag leader shape is `H`, `V`, `H+V` or `V+H`; a `D` (diagonal) on a tag of this job → delete it (`delete_element`) and place it again with `roomtag_place_ortho`.
6. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_tags_missing` again: `Untagged` = 0 on every view, or each room left is in `Problems` with its reason (→ Việc tồn).
- On the image: no tag crosses a wall, a floor edge, a dim, a tag or a text; leaders straight with at most one right-angle elbow, horizontal / vertical on the paper (`tag_leaders_info`: no `D`); each tag or leader dot sits near the room's diagonal centre.

## Skip only if

The view shows no rooms (`Rooms` = 0), or every room is already tagged (`Untagged` = 0).

## Never

- A tag head across a wall or a floor edge (RT2); a diagonal leader.
- Pick the type yourself on a new job (RT1).
- Edit a room; a room with no name / number stays "?" and is reported (R1).
