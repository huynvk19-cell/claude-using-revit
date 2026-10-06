---
name: drafting-session
description: "Ground rules + start/end routine for any Revit drafting work through Revit MCP (dims, tags, titles, viewports, crops, templates). Use at the start of every Revit drafting task, when resuming after a break or context summary, or when the user says tiếp tục, làm tiếp, bắt đầu, triển khai bản vẽ, sửa bản vẽ."
---

# Drafting session

Read first:
- `~/.claude/drafting-domain/drafting-work-rules.md` (Vietnamese).
- The project's `drafting-profile.md`, found in the project root or its `domain/`.
  - If it is missing, copy `~/.claude/drafting-domain/templates/drafting-profile.md` into the project.
  - Ask the user once for the blanks.

## Hard rules (recite before acting)

1. Annotation, 2D and view settings only. Never edit walls, doors, rooms, 3D grid/level extents or links.
2. No sync unless asked this turn. Never touch the models the profile marks off-limits.
3. Elevation/section commands: ONE view per call, with `timeoutSeconds`.
4. "dừng" / "tạm dừng" → stop immediately and report where you stopped.
5. No "Claude"/"AI" in anything written to the model.
6. Hide via View Template, not Hide in View.

## Start

1. Re-anchor: `hello_revit` or `get_active_view`. Never reuse ids from an earlier turn or a summary without checking.
2. Show the status window if the project has one (`work_status show`). If the user wants to watch: hide Properties and Project Browser (`ui_view_focus {action:"toggle", which:"properties"}`, then `"browser"`) and open + zoom each view before working on it (`ui_view_focus {action:"open", viewId}`); toggle them back at the end.
3. Find the task source (comment PDF, TASKS.md or the user's message) and the latest log in `review/<date>_<code>/`.

## During

- Preview first, then apply with `logPath`.
- New dims use the profile's **check** dim type.
- After each view: export an image and check it (`drafting-visual-check`).
- Give the user a one-line progress update when they have not heard from you for a while.

## End of turn

1. Write a log entry to `log/YYYY-MM.md`.
2. Add new lessons:
   - general → the matching domain file;
   - project-only → the profile.
3. Close the status window.
4. Report:
   - exact Revit names (sheet no. – name, view, View Template);
   - a table of what was done;
   - end with "Cần xem" and "Việc tồn".
