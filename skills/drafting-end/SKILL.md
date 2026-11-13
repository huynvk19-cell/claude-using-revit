---
name: drafting-end
description: "End a Revit drafting session: nothing left running, log entry, report. Use for kết thúc, xong việc, nghỉ, end work, done for today."
---

# End work (kết thúc làm việc)

**Tools** (load all in one ToolSearch call): `mark_types`.

No model change except the last mark (step 2). Do **not** sync unless the user asks in this turn.

## Steps

1. **Stop cleanly**: no command still running. If the user said "dừng", stop where you are and say where.
2. **Mark**: the batch's last `mark_types {action:"mark", suffix, statePath, mode:"apply", logPath}` (already-marked elements are skipped). Then `mark_types {action:"list", suffix}` for the report.
3. **Log**: write the entry in `log/YYYY-MM.md` (what was done, views, log files of `apply`), and add new lessons (general → the domain file of that topic; project only → the profile), as `drafting-session` says.
4. Leave the views open. The user decides what to close.

## Report

Follow `drafting-session`:
- exact sheet and view names worked on;
- a table of what was done;
- end with **Cần xem** and **Việc tồn** (elements still in mark types, per type).

## Never

- Sync, save-as or close the model.
- Restore the official types unless the user asks.
- Close the user's view windows.
