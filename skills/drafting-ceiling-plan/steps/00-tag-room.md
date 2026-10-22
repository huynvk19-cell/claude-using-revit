# 00 · Tag room (tag phòng)

**Skill** `drafting-ceiling-plan` · **Standard** `drafting-annotation.md` §2 RT1–RT8 · **Tools** `room_tags_missing`, `roomtag_place_ortho`, `room_tags_score`, `room_tags_broken`, `tag_leaders_info`, `delete_element`, `mark_types`, `export_sheet_images`
**Applies to** every ceiling plan view of the job.

## Do

1. **Type (RT1).** New job → ask the user once which room tag type (list the types already on the sheet). Add-on request ("tag các room còn thiếu") → leave `typeName` empty: the command takes the type of the nearest tagged rooms (`NeighbourType`).
2. **Find and place (RT2–RT4).** One view per call (`outPath`); on a ceiling plan the command checks ceiling edges instead of floor edges:
   ```
   room_tags_missing {viewIds:[one view], typeName, tagSizeMm, outPath:"<review>/p1-<viewId>.json"}
   ```
   Per view: `Untagged`, `Inside` (no leader), `Outside` (leader); with `outPath` the `Items` + `Problems` go to the file (one view per call, short result). Kept clear: walls, floor / ceiling edges, dims, tags, texts, grid bubbles (`bubbleMm`, default 10), other room tag leaders; a leader never runs through a tag, a text or another leader dot (RS0). RT5–RT8: rooms go smallest area first; every room gets an item (the search widens to 2x, 4x `maxLeaderMm`, then allows a leader to cross (never lie on) another leader, then over text, then touching model lines, last the room centre: notes `CHECK` / `FORCED` → Cần xem); shortest leader first (straight, else one right angle `path` HV / VH); a spot in line (same row / left edge) with a nearby tag wins over a slightly shorter leader. No `tagSizeMm` in the profile → the command uses the box of a placed tag (wider than the text); say so in the report.
3. Per view, open it (`ui_view_focus {action:"open", viewId}`), then place: the positions are the tag BODY centre and `roomtag_place_ortho` centres the body there (a room tag head is not its body centre; `annot_place` would shift the tag and bend the leader). Every room is tagged (RT6), also rooms of other levels seen in the view. Undo: `roomtag_place_ortho {mode:"undo", logPath}`.
   ```
   roomtag_place_ortho {mode:"apply", viewId, itemsPath:"<review>/p1-<viewId>.json", logPath:"<review>/place-p1-<viewId>.json", brief:true}
   ```
4. `room_tags_broken` on the views: a new tag with a leader showing "?" (narrow room) → delete that tag (`delete_element`) and place it inside without a leader (`roomtag_place_ortho`, no `ex/ey`), or list it.
5. Leaders (all tags, `drafting-annotation.md` "Leader của tag"): straight, one right-angle elbow when needed, horizontal / vertical on the paper. `tag_leaders_info {viewId}` per view: every room tag leader shape is `H`, `V`, `H+V` or `V+H`; a `D` (diagonal) on a tag of this job → delete it (`delete_element`) and place it again with `roomtag_place_ortho`.
6. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_tags_missing` again: `Untagged` = 0 on every view (RT6). A room left only when Revit cannot tag it in that view: a tag made there has no box / shows "?" because the view range cut plane is above the room top (`view_range_info`) → Việc tồn (view setting for the user), never silently.
- On the image: no tag crosses a wall, a ceiling edge, a dim, a tag or a text; leaders straight with at most one right-angle elbow, horizontal / vertical on the paper (`tag_leaders_info`: no `D`); each tag or leader dot sits near the room's diagonal centre.

## Skip only if

The view shows no rooms (`Rooms` = 0), or every room is already tagged (`Untagged` = 0).

## Never

- A tag head across a wall or a ceiling edge (RT2); a diagonal leader.
- Pick the type yourself on a new job (RT1).
- Edit a room; a room with no name / number stays "?" and is reported (R1).

## Score (0–10)

Rules RS0–RS4 (`drafting-annotation.md` §2). **Card score of the view = 0** when any leader lies on another leader or runs through another leader's dot (RS0). Otherwise each tag starts at 10:
- RS1 its leader lies on another leader → 0 at once.
- RS2 the leader has a right-angle bend → −1.
- RS3 the leader is longer than 3× the long side of its room → −9; longer than 2× → −5.
- RS4 the tag sits on another room → −5.

View score = average of its tags (`room_tags_score {viewIds, tagSizeMm}`; `room_tags_missing` already picks the spot with the best score). **Accepted only above 7** (`room_tags_score` → `CardScore`, `Accepted`). Report the score of every view, before and after. Below 7: dissect (which rule cost the points, how many tags, root cause, what to change), change it, run again (`drafting-work-rules.md` §2b2).
