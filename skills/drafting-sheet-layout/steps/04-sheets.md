# 04 · Number, name and create sheets (đánh số, đặt tên, tạo sheet)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §3 (HS3), §8 (HS6), LO2, LO10 · **Tools** `cur_task`
**Applies to** the sheets of the job.

## Do

1. **Group the drawings into sheets**:
   - one topic and one main scale per sheet (LO2);
   - view density between 40 and 85 % of the drawing area (LO10);
   - per house type / block / zone order (Z5);
   - several small views of the same topic go on one sheet (e.g. 2–4 narrow townhouse plans, 2 elevations).
2. **Number** each new sheet in its group's range, with the prefix and format of the profile. Leave gaps for later sheets. Sheets already issued keep their number.
3. **Name** each sheet: drawing kind + level / zone / type, in capitals, in the set's language; typical floors with their level range (§8).
4. Show the sheet table to the user and ask once:

   | Number | Name | Views | Scale | New / existing |
   |---|---|---|---|---|

5. **Create** the new sheets in the drawing list:
   - `cur_task {task:"schedule_table_write", args:{schedule, add:[{number, name}], dryRun:true}}`;
   - then the same call without `dryRun`.

   The title block is the one most used by the list; the print set and No. follow by themselves.
6. A rename / renumber of an existing, not yet issued sheet goes through the same task (`changes`), only after the user's yes.

## Done when

- Every target drawing has a sheet number and a sheet name.
- Every new sheet exists in the drawing list (task result), and its title block is the profile's.
- `cur_task {task:"print_sets_status"}` shows the print set in sync.

## Skip only if

Every target drawing already sits on a sheet with a number and a name that follow §3 and §8.

## Never

- Renumber or rename an issued sheet without the user's yes (HS3).
- Pass `force:true` on a print set someone else changed last, without asking (`drafting-views-sheets.md` §5).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
