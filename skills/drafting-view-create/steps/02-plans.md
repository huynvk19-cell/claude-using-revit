# 02 · Plans: floor, finish, roof, ceiling, site (mặt bằng)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1, VC2, VC5, VC6 · **Tools** `view_crop_info`, `crop_side_to_grids`, `query_elements_with_filter` · **Planned** `view_create`
**Applies to** the creation table rows of kind floor plan, duplicate, ceiling plan (MB sàn, lát sàn, mái, trần, tổng thể, định vị).

## Do

1. Order of creation:
   1. floor plans of every level (`plan`);
   2. roof / terrace (`roof`);
   3. site plans (`site`, `site-setout`);
   4. ceiling plans (`ceiling`);
   5. then the finish plans as **Duplicate (no detailing)** of the floor plan of the same level (VC6).
2. Every plan:
   - name by VC2;
   - template of its key;
   - scale of the register;
   - crop on + annotation crop on.
3. **One crop for the whole series** (VC5): the crop of the first floor plan (reference) is copied to every plan of the series. Same scope box if the profile has one; otherwise the same crop box (check with `view_crop_info`: same min / max in model mm).
4. With `view_create`: one call per batch of up to 10 views, `preview` then `apply` with `logPath`.

   Without it: hand the user this card's rows of the creation table (kind, level, template, scale, name). Read back with `query_elements_with_filter` once the user says done.
5. Write `View id`, `Tên view`, `Template`, `Crop` (on / same as reference) into the register.

## Done when

- Every plan row of the table has a view: right kind, level, name (VC2), template, scale.
- Crop on, and the same crop across the series (`view_crop_info`).

## Skip only if

The table has no plan row.

## Never

- Duplicate with detailing unless the user asked (VC6).
- Set the crop by hand per level when the series shares one.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
