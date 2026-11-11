# 04 · Sheet parameters, Project Browser group (tham số sheet)

**Skill** `drafting-sheet-create` · **Standard** `drafting-sheet-create.md` SC7, SC8 · **Tools** `cur_task`
**Applies to** the sheets of the job.

## Do

1. **Common parameters** (profile: stage, issue date, drawn / checked / approved by…):
   - one `cur_task {task:"sheet_list_fill", args:{schedule, param, value}}` per parameter, for the whole list;
   - only the parameters the profile names.
2. **Per-sheet parameters** (Project Browser group, house type / block, zone):
   - `cur_task {task:"schedule_table", args:{schedule}}` once, for the column keys;
   - then one `cur_task {task:"schedule_table_write", args:{schedule, changes:[…], dryRun:true}}` and the same call without `dryRun`.
3. Signer columns hidden / shown as the profile says (`cur_task sheet_list_signers`).

## Done when

- Every sheet of the job has the common parameters of the profile.
- Every new sheet has its Project Browser group value (and type / zone if the profile uses them).

## Skip only if

The profile names no common and no per-sheet parameter.

## Never

- Fill a parameter on sheets outside the job.
- Overwrite a value someone else typed without asking (`force`).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
