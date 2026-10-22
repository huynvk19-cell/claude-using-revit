# 07 · Drawing list and print set (danh mục bản vẽ, print set)

**Skill** `drafting-views-sheets` · **Standard** `drafting-views-sheets.md` §5 · **Tools** `print_set_info`, `print_set_from_schedule`
**Applies to** every view of the job (crop, grid ends, template), or the sheets of the job (viewport type, titles, drawing list).

## Do

When a sheet was added, renamed or renumbered:
- check the sheet is in the drawing-list schedule named in the profile;
- `print_set_info {setName, scheduleName}`; anything missing → `print_set_from_schedule {mode:"apply", replace:true}`;
- log it, report the sheets before / after.

## Done when

- The print set = the drawing list; sheets before / after reported.

## Skip only if

No sheet was added, renamed or renumbered.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
