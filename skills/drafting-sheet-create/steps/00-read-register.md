# 00 · Read the register, gate the rows (đọc sổ, lọc dòng)

**Skill** `drafting-sheet-create` · **Standard** `drafting-sheet-create.md` SC1, `drafting-sheet-layout.md` §11 · **Tools** `cur_task`, `sheet_browser_names`
**Applies to** once per job (every register row).

## Do

1. Open the latest `sheet-register-*.md` of the job (round folder or `log/`).
2. Gate each row:
   - **in**: state `planned`, `Vừa khổ` filled (card 03 of `drafting-sheet-layout` done);
   - **already has a sheet**: `Có sẵn` = ok / chưa đặt with a sheet → copy its number, name and id, state `sheet`;
   - **not ready**: `Vừa khổ` empty → stop, run `drafting-sheet-layout` card 03 first.
3. Read the drawing list once: `cur_task {task:"schedule_sheets", args:{schedule}}` (numbers and names already used).

## Done when

- Every row is either `in` (to create), `sheet` (has one), or listed as not ready with the reason.
- The used numbers and names of the drawing list are known (for card 01).

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
