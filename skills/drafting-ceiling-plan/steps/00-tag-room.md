# 00 · Tag room (tag phòng)

**Skill** `drafting-ceiling-plan` · **Standard** `drafting-annotation.md` §2 RT1–RT5 · **Tools** `room_tags_missing`, `annot_place`, `room_tags_broken`, `tag_leaders_info`, `delete_element`, `mark_types`, `export_sheet_images`
**Applies to** every ceiling plan view of the job.

## Do

1. **Type (RT1).** New job → ask the user once which room tag type (list the types already on the sheet). Add-on request ("tag các room còn thiếu") → leave `typeName` empty: the command takes the type of the nearest tagged rooms (`NeighbourType`).
2. **Find and place (RT2–RT5).** Ceiling plans may go many per call; on a ceiling plan the command checks ceiling edges instead of floor edges:
   ```
   room_tags_missing {viewIds:[batch], typeName, tagSizeMm}
   ```
   Per view: `Untagged`, `Inside` (no leader), `Outside` (leader), `Problems`, `Items`. Only rooms of the view's own level count; a room tagged in a sibling dependent view (overlap strip) counts as tagged, and a room seen in two sibling views of one call is proposed once (`ProposedInSibling`). A room cut by the crop gets its leader dot in the part that is seen. No clear spot near the room → the body goes farther out (`farLeaderMm`, note "far"), still with a leader to the room. Tags near each other are lined up in rows / columns (RT5, `alignMm`, `nearMm`; note "aligned"). No `tagSizeMm` in the profile → the command uses the body box of a placed tag without leader; say so in the report.
   A room in `Problems` with "no clear place" → run again with `roomIds:[id], explain:true` to see what blocks it. Skip an obstacle kind (`ignore`, e.g. a hatch or furniture category) only with the user's yes; otherwise list the room.
3. Per view, open it (`ui_view_focus {action:"open", viewId}`), then
   ```
   annot_place {mode:"preview", viewId, items:<Items>}
   annot_place {mode:"apply",   viewId, items:<Items>, logPath:"<review>/room-tags-<viewId>.json"}
   ```
   For a room tag, `right` / `up` is the centre of the tag **body** (the leader starts there); `annot_place` turns the leader on before it moves the head, so a body outside the room is accepted.
4. `room_tags_broken` on the views: a new tag with a leader showing "?" (narrow room) → delete that tag (`delete_element`) and place it inside without a leader (`annot_place`, `leader:false`), or list it.
5. Leaders (all tags, `drafting-annotation.md` "Leader của tag"): straight, one right-angle elbow when needed, horizontal / vertical on the paper. `tag_leaders_info {viewId}` per view: every room tag leader shape is `H`, `V`, `H+V` or `V+H`; a `D` (diagonal) on a tag of this job → delete it (`delete_element`) and place it again with `annot_place` (it sets the elbow).
6. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_tags_missing` again: `Untagged` = 0 on every view, or each room left is in `Problems` with its reason (→ Việc tồn).
- On the image: no tag crosses a wall, a ceiling edge, a dim, a tag or a text; leaders straight with at most one right-angle elbow, horizontal / vertical on the paper (`tag_leaders_info`: no `D`); each tag or leader dot sits near the room's diagonal centre; tags near each other line up (RT5).

## Skip only if

The view shows no rooms (`Rooms` = 0), or every room is already tagged (`Untagged` = 0).

## Never

- A tag head across a wall or a ceiling edge (RT2); a diagonal leader.
- Pick the type yourself on a new job (RT1).
- Edit a room; a room with no name / number stays "?" and is reported (R1).
