# 07 · Verify and update the register (kiểm, cập nhật sổ)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` VC12 · **Tools** `query_elements_with_filter`, `view_crop_info`, `sheet_browser_names`
**Applies to** every register row in state `sheet`.

## Do

1. Read back once:
   - `query_elements_with_filter {category:"Views", returnFields:["View Name","View Template","View Scale","Sheet Number"]}`;
   - `view_crop_info` per batch of up to 10 views.
2. Check every row against VC12:
   - id exists;
   - name = VC2;
   - template = its key;
   - scale = register;
   - crop on (series: same crop);
   - not on a sheet yet.
3. Rows that pass → state `view`. Rows that fail → back to the card of their kind (02–06), with the reason in `Ghi chú`.
4. Report counts: created, reused, failed (with the reason).

## Done when

- Every register row that is not `n/a` is `view` (or later), with `View id`, `Tên view`, `Template`, `Crop` filled.
- Nothing is placed on a sheet yet by this skill.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
