# 05 · Verify and update the register (kiểm, cập nhật sổ)

**Skill** `drafting-sheet-create` · **Standard** `drafting-sheet-create.md` SC10 · **Tools** `cur_task`, `sheet_browser_names`
**Applies to** the sheets of the job.

## Do

1. Read back once:
   - `cur_task {task:"schedule_sheets", args:{schedule}}`: number, name, id, row order;
   - `cur_task {task:"print_sets_status"}`: in sync;
   - `sheet_browser_names`: names and groups as the Project Browser shows them.
2. Copy each sheet id into `Sheet id` of its register rows. Set those rows to `sheet`.
3. Compare each row with the register; the gaps go back to the card that caused them:
   - a name or number off → card 01 / 03;
   - a wrong group → card 04;
   - a missing sheet → card 03.

## Done when

- Every register row that is not `n/a` has a `Sheet id` and is `sheet` (or later).
- The print set is `inSync`; the Project Browser shows every new sheet under its group with its exact name.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
