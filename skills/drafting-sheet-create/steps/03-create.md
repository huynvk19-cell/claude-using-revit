# 03 · Create the sheets (tạo sheet)

**Skill** `drafting-sheet-create` · **Standard** `drafting-sheet-create.md` SC6 · **Tools** `cur_task`
**Applies to** the new sheets of card 01.

## Do

1. One call for all new sheets, dry run first:
   `cur_task {task:"schedule_table_write", args:{schedule:"<drawing list>", add:[{number, name}, …], dryRun:true}}`
2. Check the dry run:
   - the count = the new sheets of card 01;
   - the title block = card 02;
   - no number refused.
3. Run the same call without `dryRun`.
4. The print set and No. of the same name follow by themselves. A set that someone else changed last comes back unchanged: ask before `force:true`.
5. Log it: count, numbers, the print set result.

## Done when

- Every new sheet of card 01 exists (task result lists it) with the profile's title block.

## Skip only if

Card 01 has no new sheet.

## Never

- Create the sheets one call per sheet (one call, the whole list).
- Run without the dry run.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
