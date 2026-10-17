# 04 · Wall tags (tag tường)

**Skill** `drafting-plan-dims` · **Standard** `drafting-plan-dims.md` §3 WT1–WT5 · **Tools** `query_elements_with_filter`, `rooms_dump`, `room_at_point`, `view_elem_boxes`, `tag_leaders_info`, `annot_owners`, `annot_place`, `tag_reposition`, `delete_element`, `annotation_overlaps`, `mark_types`, `ui_view_focus`, `export_sheet_images`
**Applies to** every floor plan view of the job, except OVERALL / key plans, the areas the profile keeps to grid dims only and the toilet areas (WT4). Runs last, so the wall tags keep clear of every dim, door tag, room tag and floor spot already placed.

## Do

1. **Type (WT1).** Use a wall tag type that already exists: the profile's wall tag type if it fixes one; else the wall tag type used most in the view (`tag_leaders_info {viewId}` lists tag types); else used most in the project. No wall tag type in the project, or two in use and no clear choice → ask the user directly (list what is there) and wait. Never load or make a wall tag family yourself.
2. **Walls to tag (WT2).** Per view: the host walls the plan draws (`query_elements_with_filter` category Walls, visible in the view; `view_elem_boxes` Box ≠ null). One tag per wall run: a straight run of one wall type, joined end to end, gets one tag. A wall that already has a wall tag (anyone's) counts as done.
3. **Drop the toilet areas (WT4).** Rooms whose name holds a toilet name part of the profile (default `TOILET`, `WC`): `rooms_dump` / `room_at_point` on both faces of each wall. A wall bounding or inside a toilet room gets no tag (drawn in the toilet plan set). A long wall that only partly faces a toilet → tag it on the part outside the toilet.
4. **Whose tags** (before moving or deleting any): `annot_owners {viewId, listIds:true}`. Own (profile Revit user as LastChangedBy) or marked type → may fix. Someone else's → list, unless the user allowed it this session.
5. **Place** (WT3, WT5), per view: open it (`ui_view_focus {action:"open", viewId}`), read the free space (`view_elem_boxes {viewId, allAnnotations:true}` + wall `ViewBox`es). For each wall run pick a free stretch of wall face (not over a door / window, not under a dim, tag or text), on the room side (exterior wall → inside the building, the outside is the dim band). Leader end = a point on that wall face; head = straight out from it, perpendicular to the wall, the tag box ~2 mm paper clear of the face. End and head share one coordinate (same `up` for a vertical wall, same `right` for a horizontal wall), so the leader is one straight segment with no elbow.
   ```
   annot_place {viewId, mode:"preview", items:[{kind:"tag", elementId:<wall>, typeName, right, up, leader:true, endRight, endUp}]}
   annot_place {viewId, mode:"apply",   items:[...], logPath:"<review>/wall-tags-<viewId>.json"}
   ```
6. **Check the boxes**: `view_elem_boxes {viewId, allAnnotations:true}` on the new tags. A tag box that touches or crosses the wall (WT3) → `tag_reposition` its head straight out a little further (end unchanged); a box over a dim, tag, room tag, spot or text → slide the tag along the wall to the next free stretch (head and end together). Walls with no free stretch → list.
7. **Leaders**: `tag_leaders_info {viewId}`: every wall tag of this job has a leader (WT5) and its shape is `H` or `V` (straight, perpendicular to the wall). No leader, an elbow or a `D` → fix with `tag_reposition`, or `delete_element` and place again.
8. `annotation_overlaps {viewId}`; mark types (`drafting-session`); export the views and check by image (`drafting-visual-check`).

## Done when

- Every wall run drawn on the view, outside the toilet areas and grid-dims-only areas, has one wall tag, or is listed with its reason (no free stretch, someone else's tag in the way).
- `tag_leaders_info`: every wall tag of this job has a leader, shape `H` / `V`, perpendicular to its wall; `annotation_overlaps`: no wall tag over a dim, tag or text.
- On the image: each wall tag sits close to its wall, its box clear of the wall lines, leader short and straight to the face; no wall tag in a toilet room.

## Skip only if

The view draws no host wall outside the toilet / grid-dims-only areas, it is an OVERALL / key plan, or every wall run already has a wall tag. Say which.

## Never

- Load, make or pick a wall tag type that is not already in the project; ask instead (WT1).
- A wall tag box on the wall lines; a wall tag without a leader; an elbowed or diagonal leader.
- Tag walls of a toilet area (WT4).
- Move or delete someone else's wall tags without the user's go-ahead this session; edit a wall (R1).
