# 03 · Scale, paper fit, zones (tỷ lệ, vừa khổ, chia vùng)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §5, §7 (Z1–Z5) · **Tools** `view_crop_info`
**Applies to** every view of the job (rows `ok` and `not placed` of card 02).

## Do

1. `view_crop_info` for every view (one call per batch of up to 10 views): crop size, annotation crop offsets, scale, scope box.
2. Paper size of each view:

   paper (mm) = crop + annotation crop (m) × 1000 / scale

   Compare it with the drawing area of the profile, minus the margins (LO1).
3. Check each view's scale against the scale table of the standard and of the sheet-set file:
   - a scale outside the table, or outside the standard series → flag it;
   - the user changes the scale, or it goes to Việc tồn.
4. A view does not fit → follow the order of §5:
   1. the next smaller scale allowed for that kind of view;
   2. otherwise zones (Z1–Z5): propose **one** zone scheme for every level and every plan kind, with boundaries on grids or joints and one bay of overlap.

   Show the scheme as a table: zone → grid range → levels. The user makes the scope boxes and dependent views, or approves the existing ones.
5. Write per view into the target list file: paper size, fits yes / no, scale ok / flag, zone.

## Done when

- Every view of the job fits the drawing area at a scale from the table, or it is split into zones that do.
- The zone scheme (if any) is the same for every level and every plan kind (Z1). Each zone has a key plan source (Z3) and a level overview plan (Z4) in the target list.

## Skip only if

Every view fits at a table scale and no zone is needed (all rows "fits yes, scale ok").

## Never

- Change a view scale, crop or scope box without the user's yes. Crops follow `drafting-views-sheets` card 02.
- Propose different zone boundaries for different levels.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
