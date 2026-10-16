# 03 · Floor spot elevations (tag cao độ sàn)

**Skill** `drafting-plan-dims` (same card in `drafting-plan-floor-finish`) · **Standard** `drafting-annotation.md` §2b FL1–FL3 · **Tools** `room_spots_missing`, `stair_door_spots`, `annot_place`, `delete_element`, `annotation_overlaps`, `mark_types`, `ui_view_focus`, `export_sheet_images`
**Applies to** every floor plan view of the job, except OVERALL / key plans. Runs after the room tags, so the spots keep clear of them.

## Do

1. **Type (FL3).** The profile fixes a spot elevation type → use its check type. Otherwise: new job → ask the user once (list the spot types already on the sheet); add-on request ("bổ sung cao độ còn thiếu") → leave `typeName` empty: the command takes the type used most in the view. Never pick one yourself.
2. **Rooms (FL1).** Plans may go many per call:
   ```
   room_spots_missing {viewIds:[batch], typeName, minAreaM2:<profile, default 5>, stairRoomText:<profile>}
   ```
   Per view: `Rooms`, `HasSpot`, `Missing`, `Nested` (small rooms inside another room: no spot, list them in the report), `StairRooms` (step 4), `Problems`, `Items`.
3. Per view, open it (`ui_view_focus {action:"open", viewId}`), then
   ```
   annot_place {mode:"preview", viewId, items:<Items>}
   annot_place {mode:"apply",   viewId, items:<Items>, logPath:"<review>/floor-spots-<viewId>.json"}
   ```
   Check each spot's `ElevationMm` against the room's level (a value far off = wrong floor under the point: delete it, `delete_element`, and list the room).
4. **Stair enclosures (FL2)**: inside and outside every stair door, same level or not:
   ```
   stair_door_spots {mode:"plan",  viewIds:[batch], typeName:<check spot type>, stairRoomText, stairViewText, civilSymbolId:<profile, optional>}
   stair_door_spots {mode:"apply", viewIds:[batch], ..., logPath:"<review>/stair-door-spots.json"}
   ```
   Rows `existing spot – skipped` are fine; `no surface found` / `surface not shown` → list.
5. `annotation_overlaps {viewId}` per view; a new spot over a tag, a dim or a text → delete it and place it again a little further inside the room (`annot_place`).
6. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_spots_missing` again: `Missing` = 0 on every view, except rooms in `Problems` with their reason (→ Việc tồn); `Nested` rooms listed in the report.
- Every stair door seen has a spot inside and outside (`stair_door_spots` plan: every row `existing spot – skipped` or listed).
- On the image: each spot inside its room, off walls, dims, tags, texts and door swings.

## Skip only if

The view shows no rooms, it is an OVERALL / key plan, or every room already has its spot and every stair door both sides (both commands report nothing to add).

## Never

- Pick the spot type yourself on a new job (FL3); move or delete spots the user placed by hand.
- A spot on a linked floor or on the wrong level; a spot in a small room inside another room unless the user asks.
- Edit a room or a floor (R1).
