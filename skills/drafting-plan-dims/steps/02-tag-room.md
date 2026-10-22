# 02 · Tag room (tag phòng)

**Skill** `drafting-plan-dims` · **Standard** `drafting-annotation.md` §2 TR1–TR8 · **Tools** `room_tags_missing`, `roomtag_place_ortho`, `room_tags_score`, `delete_element`, `mark_types`, `export_sheet_images`
**Applies to** every floor plan view of the job.

## Do

1. **Score before.** `room_tags_score {viewIds, tagSizeMm}` on the views: `CardScore`, `Untagged` per view.
2. **Type.** `typeName` from the profile; none → ask the user once (never pick one yourself). Add-on request ("tag các room còn thiếu") → leave `typeName` empty: the command takes the type of the nearest tags.
3. **Find the spots**, one view per call:
   ```
   room_tags_missing {viewIds:[one view], typeName, tagSizeMm, outPath:"<review>/tr-<viewId>.json"}
   ```
   Inside the room when the tag fits (leader 0), else the shortest leader that scores best (straight or one right angle, dot just inside the room). Every untagged room gets an item; `Problems` lists the ones beyond `zeroAtMm`, crossing a tag / text (CHECK) or put at the room centre (FORCED) → Cần xem.
4. **Place**, per view: open it (`ui_view_focus {action:"open", viewId}`), then
   ```
   roomtag_place_ortho {mode:"apply", viewId, itemsPath:"<review>/tr-<viewId>.json", logPath:"<review>/place-tr-<viewId>.json", brief:true}
   ```
   Undo: `roomtag_place_ortho {mode:"undo", logPath}`.
5. **Score after.** `room_tags_score` again (`outPath` for every tag). A tag at 0 that this job placed (TR3–TR6): delete it (`delete_element`) and run 3–4 again for that room (`roomIds`).
6. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- `room_tags_score`: `Untagged` = 0 on every view (TR8) and `CardScore` > 7 (`Accepted`).
- No tag of this job at 0 (TR3–TR6).

## Skip only if

The view shows no rooms (`Rooms` = 0).

## Never

- A tag body on a wall or a floor edge (TR3); a leader on another leader, along a wall or a floor edge, or through another leader's dot (TR4–TR6).
- Pick the tag type yourself on a new job.
- Move, retype or delete room tags of other drafters; edit a room.

## Score (0–10)

Rules TR1–TR8 (`drafting-annotation.md` §2), computed by `room_tags_score`. Each tag: full score with no leader in its room, falling evenly to 0 at a `zeroAtMm` leader (TR1); +2 when lined up with another tag in a row or column (TR2); 0 on a wall / floor edge (TR3), with its leader on another leader (TR4), along a wall / floor edge (TR5) or through another leader's dot (TR6); −3 for each other room its leader runs through (TR7); clamped 0–10. View card score = average of its tags; **0 when any room of the view has no tag** (TR8). **Accepted only above 7.** Report the score of every view, before and after. Below 7: dissect (which rule cost the points, how many tags, root cause, what to change), change it, run again (`drafting-work-rules.md` §2b2).
