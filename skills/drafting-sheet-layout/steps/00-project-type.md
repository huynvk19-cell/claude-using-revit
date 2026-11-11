# 00 · Building type and stage (loại công trình, giai đoạn)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §1, §2, §5 · **Tools** `cur_task`, `sheet_views_ids`
**Applies to** once per job (the whole set).

## Do

1. Read from the profile:
   - the building type, mix and sub-type;
   - the stage;
   - the paper size, title block and drawing area;
   - the number prefix and format, house type / block codes, zones.
2. Blank values → look at the model for hints, read-only:
   - `cur_task {task:"schedule_list"}` gives the drawing-list schedule;
   - `sheet_views_ids` on the prefix gives the title block in use and the scales;
   - levels, the size of the footprint.

   Show the hints and **ask the user once** for everything still blank. Never assume the type or the stage.
3. Pick the sheet-set file from table §1 of the standard:
   - mixed building → the main part's file + the named extra sections;
   - type not in the table → the core set only, and note it.
4. Write the answers into `drafting-profile.md` (rows "Layout: …") and log them.

## Done when

- Profile has: building type (+ mix), stage, paper size, title block, drawing area (margin, title strip, notes column, mm), number prefix + format, zone order, sheet name language.
- The one sheet-set file of the job is named in the checklist header.

## Skip only if

The profile already holds every value above and the user did not change the type or the stage.

## Never

- Open more than one sheet-set file.
- Change the paper size or title block of a set that is already issued.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
