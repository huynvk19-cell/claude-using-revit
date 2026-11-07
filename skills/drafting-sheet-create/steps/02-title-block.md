# 02 · Title block check (kiểm khung tên)

**Skill** `drafting-sheet-create` · **Standard** `drafting-sheet-create.md` SC5 · **Tools** `sheet_views_ids`, `cur_task`
**Applies to** once per job.

## Do

1. Find the title block the drawing list uses most:
   - `sheet_views_ids {sheetPrefix, includeAll:true}`;
   - or the `dryRun` of card 03, which names the title block it will use.
2. Compare it with the title block of the profile.
3. Act on the result:
   - **Same** → go on.
   - **Different, or the list has no sheet yet** → stop. Ask the user to create the first sheet by hand with the right title block, then read again.
   - **Placeholder in the result** → same as above.

## Done when

- The title block the creation will use = the profile's title block (shown by a tool result of this turn).

## Skip only if

Never.

## Never

- Create sheets with another title block, then plan to change them later.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
