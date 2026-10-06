---
name: drafting-opening-tags
description: "Tag every visible window and door (incl. roll-up doors) on elevation and section views: exactly one tag per visible opening, above + centred + touching with a short leader, lifted or beside when blocked, never over dims/tags/hard elements, same height per row; fix duplicates/wrong tags. Use for tag cửa, tag cửa sổ, tag cửa đi, tag cửa cuốn, thiếu tag, tag trùng, tag đè dim, căn tag, door tags, window tags."
---

# Opening tags (elevations / sections)

Standard: `~/.claude/drafting-domain/drafting-opening-tags.md`. Rules T1–T8 decide every case. Read it first.

## Steps — ONE view per call (`viewIds:[one id]`, `maxSeconds` set)

1. **Audit** with `door_window_tag_check {mode:"audit", viewIds:[id], viewTypes:["Section","Elevation"], untaggedOnlyInTaggedViews:false}`. It lists:
   - untagged openings;
   - duplicate tags (two tags on one opening);
   - orphaned tags, and tags showing "?" or empty;
   - tag heads crossed by the opening's own linework.
2. **Preview:**
   ```
   elevation_opening_tags {viewIds:[id], mode:"preview", addLeader:true, maxSeconds:90}
   ```
   The preview:
   - finds the openings that are really visible (rays);
   - plans a tag for each untagged one;
   - reports wrong existing tags: on host, door tag not above, window tag beside, far, covering something.
3. **Apply:**
   ```
   elevation_opening_tags {viewIds:[id], mode:"apply", addLeader:true, fixExisting:true, logPath}
   ```
   - `fixExisting` moves only the wrong tags. Never pass `repositionAll` on a view the user adjusted by hand (T8).
   - Tags that still cover a dim or a hard element are coloured red.
4. **Duplicates and orphans:**
   - Delete the extra tag of a duplicated opening. Keep the one that is placed better.
   - Report orphaned and "?" tags; do not touch the model.
5. **Rows:** `tag_align {viewId, mode:"apply", refTagText, tagTexts:[...], logPath}` brings the tags of one row to the same height above their openings, with leaders (T7).
6. **Roll-up doors:** check the leader end sits about 2.5 mm inside the visible top, not on the coil box (T5).
7. **Verify:**
   - Run `annotation_overlaps` (tags vs dims/tags).
   - Export the sheet image and look (`drafting-visual-check`).
   - Re-run step 1 until there are no untagged or duplicate openings.
8. **Report per view:**
   - tags added, moved and deleted;
   - what is still red;
   - what is left for manual work.
   - After the user approves, `mode:"clearhighlight"` with the same `logPath` removes the red.

## Never

- Tag a hidden opening.
- Put a tag head on its own opening.
- Put a window tag beside its window.
- Batch several views in one call.
- Run `repositionAll` over hand-adjusted views.
- Undo restores everything: `mode:"undo"` with the `logPath`.
