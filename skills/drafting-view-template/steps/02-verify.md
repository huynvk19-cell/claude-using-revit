# 02 · Verify and update the register (kiểm, cập nhật sổ)

**Skill** `drafting-view-template` · **Standard** `drafting-view-template.md` VT6, VT10 · **Tools** `view_templates_apply`, `sheet_views_ids`
**Applies to** every view of the register.

## Do

1. Run the preview of card 01 again with the same items: every view must come back as `Ok`.
2. Views already on sheets: `sheet_views_ids` once per sheet prefix shows their template; it must match the mapping table.
3. Check the rows side by side (VT6): same `Loại`, same view kind, same scale → same template.
4. Write the real template name into column `Template` of every row (`—` for a view kind that takes none, `!` + reason for a view not done).
5. Report counts: set, replaced, already right, left as they were (with the reason).

## Done when

- Every register row with a `View id` has `Template` filled.
- The second preview shows no `Set`, no `Replaced`, and only `Problems` that have a reason in `Ghi chú`.

## Skip only if

Never.

## Score (0–10)

- −2 for each row whose `Template` is empty.
- −2 for each series of one kind under two templates with no line in the profile (VT6).

**Accepted only above 7** (`drafting-work-rules.md` §2b2).
