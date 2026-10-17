# 05 · Slope marks (ký hiệu độ dốc)

**Skill** `drafting-plan-dims` (same card in `drafting-plan-floor-finish`) · **Standard** `drafting-annotation.md` §2c SL1–SL5 · **Tools** `floor_levels_probe`, `slope_spots_place`, `query_elements_with_filter`, `view_elem_boxes`, `annotation_overlaps`, `delete_element`, `mark_types`, `ui_view_focus`, `export_sheet_images`
**Applies to** every floor plan view of the job that shows a sloped face (roof, ramp, floor with a level change), except OVERALL / key plans. Runs after the floor spots, so the slope marks keep clear of every dim, tag, room tag and spot.

## Do

1. **Type (SL2–SL4).** `slope_spots_place {mode:"types"}`: every Spot Slopes type with `Direction`, `Units`, `Used`, and `PlanSources`. Pick: the profile's slope type → else the type with `Direction` Down and `Units` decimal or percentage used most → else any such type. None → ask the user before making a type (never change an existing type). `PlanSources` = 0 → ask the user to place one spot slope by hand on any plan (Annotate > Spot Slope): the API cannot create one, the command copies it.
2. **Sloped faces (SL1).** Per view: `floor_levels_probe {viewId}` (floors, ramps, landings, roofs). Keep the `Top` faces with 0 < `SlopePct` < 50 and `AreaM2` ≥ 1; each one gets one mark. A face that already carries a spot slope in the view counts as done (`query_elements_with_filter {category:"OST_SpotSlopes", viewId}` + their `view_elem_boxes`).
3. **Point (SL5).** Per face: the centre of its `Box`. Open the view (`ui_view_focus {action:"open", viewId}`), read the free space (`view_elem_boxes {viewId, allAnnotations:true}`); a dim, tag, room tag, spot or text near the centre → slide the point along the face to a free spot (inside the `Box`, ≥ 1 m from its edges).
4. **Place**, one call per view:
   ```
   slope_spots_place {viewId, mode:"preview", items:[{elementId, right, up, typeName}]}
   slope_spots_place {viewId, mode:"apply",   items:[...], logPath:"<review>/slope-marks-<viewId>.json"}
   ```
   An item in `Errors` ("copy stayed on …", "no top face") → move the point well inside the face and try again, else list it.
5. **Check**: `view_elem_boxes {viewId, ids:[new]}` (Box ≠ null, box clear of walls and other annotations); one over something → `delete_element` and place again at a freer point. `annotation_overlaps {viewId}`.
6. Mark types (`drafting-session`); export the views and check by image (`drafting-visual-check`): arrows point down the slope, values readable.

## Done when

- Every sloped face of SL1 on the view has one spot slope of a type with Direction Down and decimal / percentage units, or is listed with its reason (face too small, no free spot, no source spot slope yet).
- `annotation_overlaps`: no slope mark over a dim, tag or text; on the image each arrow lies on its face and points down the slope.

## Skip only if

`floor_levels_probe` finds no sloped face on the view (all `SlopePct` = 0 or only edges), or it is an OVERALL / key plan. Say which.

## Never

- A spot slope type with Direction Up, or units in degrees / 1:n (SL2, SL3); change the settings of an existing type.
- Make a new type without asking (SL4); tag a linked element; edit a floor, roof or ramp (R1).
- Move or delete someone else's spot slopes without the user's go-ahead this session.
