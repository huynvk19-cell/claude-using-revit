# 03 · Elevations and sections (mặt đứng, mặt cắt)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1, VC2, VC5, VC7 · **Tools** `view_crop_info`, `query_elements_with_filter` · **Planned** `view_create`
**Applies to** the creation table rows of kind elevation, section.

## Do

1. **Elevations**, one per direction the register asks for (chính, sau, bên):
   - marker on the ground floor plan, outside the building, looking square at the facade;
   - crop from grade − 1 m to the top of the roof + the annotation band;
   - far clip just past the facade.
2. **Sections**:
   - longitudinal through the stair and the light well; cross through the main rooms;
   - line on the ground floor plan, crop from the lowest floor (basement / footing) to the top of the roof;
   - far clip to the next wall line.
3. Names by VC7 (`MẶT ĐỨNG CHÍNH`, `MẶT CẮT A-A`…), template `elevation` / `section`, scale of the register.
4. With `view_create`: `preview` (markers / lines in model mm) → `apply` with `logPath`.

   Without it: hand the user the table:

   | Tên | Hướng nhìn | Đặt trên view | Qua (trục / phòng) | Crop đáy → đỉnh | Far clip |
   |---|---|---|---|---|---|

   Read back with `query_elements_with_filter` / `view_crop_info`.
5. Write the ids and names into the register.

## Done when

- Every elevation / section row has a view: right direction (the facade / the stair seen), name (VC7), template, scale.
- Crop and far clip as above (`view_crop_info`).

## Skip only if

The table has no elevation or section row.

## Never

- Cut a section that misses the stair when the register says "qua thang".

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
