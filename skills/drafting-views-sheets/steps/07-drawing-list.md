# 07 · Drawing list and print set (danh mục bản vẽ, print set)

**Skill** `drafting-views-sheets` · **Standard** `drafting-views-sheets.md` §5 · **Tools** `pyrevit_task`, `print_set_info`
**Applies to** every view of the job (crop, grid ends, template), or the sheets of the job (viewport type, titles, drawing list).

## Do

When a sheet was added, removed, renamed or renumbered:
- check the sheet is in the drawing-list schedule named in the profile;
- `pyrevit_task {task:"print_set_sync", args:{schedule:"<drawing-list schedule>"}}`: the print set named after the schedule gets exactly its sheets (schedule order, no placeholders) and the No. column is renumbered (same code as the user's button **Print set theo DM**); `dryRun:true` to look first;
- a set last changed by someone else comes back `skipped`: ask the user before passing `force:true`;
- log it, report the sheets before / after and the added / removed sheets from the result.

## Done when

- The print set = the drawing list (`print_sets_status` shows `inSync`); sheets before / after reported.

## Skip only if

No sheet was added, removed, renamed or renumbered.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
