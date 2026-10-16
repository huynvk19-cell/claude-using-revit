# 01 · Door / window tags (tag cửa)

**Skill** `drafting-plan-dims` · **Standard** `drafting-plan-dims.md` §2 PT1–PT6 · **Tools** `door_window_tag_check`, `query_elements_with_filter`, `view_elem_boxes`, `annot_owners`, `annot_place`, `tag_reposition`, `tag_leaders_info`, `delete_element`, `annotation_overlaps`, `mark_types`, `ui_view_focus`, `export_sheet_images`
**Applies to** every floor plan view of the job, except OVERALL / key plans and the areas the profile keeps to grid dims only. Runs after card 00, so the tags keep clear of the door dims.

## Do

1. **Audit the batch** (plans go many per call):
   ```
   door_window_tag_check {mode:"audit", viewIds:[batch], viewTypes:["FloorPlan"], untaggedOnlyInTaggedViews:false}
   ```
   Per view: `Untagged`, `Broken`, `Overlap`; issues `untagged`, `duplicate`, `orphaned`, `empty_text`, `host_not_visible`, `overlap` (tag head crossed by the frame, leaf or swing).
2. **Drop what the view does not draw** (PT1): an untagged opening under a filled / masking region, outside the crop or in a grid-dims-only area gets no tag; list it.
3. **Whose tags** (before moving, retyping or deleting any): `annot_owners {viewId, listIds:true}`. Own (profile Revit user as LastChangedBy) or marked type → may fix. Someone else's → list, unless the user allowed it this session.
4. **Type** (PT6): the door / window tag type used most in the view (`tag_leaders_info {viewId}` lists tag types); none in the view → most used in the project; none → ask.
5. **Place the missing tags**, per view: open it (`ui_view_focus {action:"open", viewId}`), read the free space (`view_elem_boxes {viewId, allAnnotations:true}` + the opening `ViewBox`es), pick the head point (PT2): centred on the opening width, ~1–2 paper mm off the wall face, door → the side without the swing, window → outside. Blocked by a dim, tag, room tag or hard element (PT3) → nearest free spot with an orthogonal leader to the opening (PT4); same wall, same row → same distance off the wall (PT5).
   ```
   annot_place {viewId, mode:"preview", items:[{kind:"tag", elementId, typeName, right, up, leader, endRight, endUp, elbowFirst}]}
   annot_place {viewId, mode:"apply",   items:[...], logPath:"<review>/opening-tags-<viewId>.json"}
   ```
6. **Fix own tags**: `overlap` (head on its door, frame or swing), head over a dim or tag, or a leader whose head still sits on the opening → `tag_reposition {viewId, mode:"preview"|"apply", items:[{id, right, up, endRight, endUp, elbowFirst}], logPath}`. `duplicate` → keep the better placed one, `delete_element` the other (own only). `orphaned` / `empty_text` → list.
7. **Leaders**: `tag_leaders_info {viewId}`: every door / window tag leader of this job is `H`, `V`, `H+V` or `V+H`; a `D` → `tag_reposition` with an elbow.
8. `annotation_overlaps {viewId}`; mark types (`drafting-session`); export the views and check by image (`drafting-visual-check`): no tag left on a hidden opening, none over a swing or a dim.

## Done when

- `door_window_tag_check` again: `untagged` = 0 except openings listed with their reason (not drawn, grid-dims-only area); no `duplicate`, no `overlap` on own tags; someone else's faults listed.
- `tag_leaders_info`: no `D` on tags of this job; `annotation_overlaps`: no door / window tag over a dim or another tag.
- On the image: each tag sits by its opening on the right side (PT2), rows line up (PT5).

## Skip only if

The view draws no door or window, it is an OVERALL / key plan, or every opening already has exactly one good tag (audit clean). Say which.

## Never

- Tag a hidden or linked opening; put a tag head on its own opening, frame or swing.
- Move, retype or delete someone else's tags without the user's go-ahead this session.
- Re-place every tag of a view the user has adjusted by hand (PT6).
- A diagonal leader; a new tag type other than the mark type.
