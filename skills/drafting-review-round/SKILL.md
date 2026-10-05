---
name: drafting-review-round
description: "Work through a reviewer comment round on Revit drawings (comment PDF + TASKS.md/CSV) task by task until done: plan, execute in Revit, verify, log, report. Use when the user gives a comment PDF/task list, says thực hiện comment, xử lý comment, đọc file và làm, review round."
---

# Review round

Read first:
- `~/.claude/drafting-domain/drafting-work-rules.md`
- the project profile
- the topic file for each task (dimensions / annotation / views-sheets)

1. **Read everything in the round folder** (PDF, TASKS.md, CSV, images). For each task, note:
   - id: T01…, or `A(1)` for groups — never `A1`
   - sheet number – name
   - view
   - what is asked
   - topic
2. **Classify each task:**
   - annotation → do it;
   - model change → list it for the user;
   - locked by a scope box or needs the UI by hand → list it as manual.
3. **Execute one task at a time**, in order:
   - Load the matching skill: `drafting-dims`, `drafting-tags-titles` or `drafting-views-sheets`.
   - Log to `review/<date>_<code>/Txx-<what>.json`.
   - Skip tasks the user says are done. Never redo them.
4. **Verify each task** with sheet images (`drafting-visual-check`) before marking it done.
5. **Stop** on "dừng". When the user says to resume, re-read the log folder to see where you are.
6. **Finish:**
   - Write a log entry.
   - Report a table: task → sheet/view → result.
   - List leftovers: manual work, model-side work, and dims still in the check type.
   - Name the sheets that changed (the user reviews the combined PDF).
