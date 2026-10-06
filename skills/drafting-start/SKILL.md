---
name: drafting-start
description: "Start a Revit drafting work session: re-anchor on the open model, open the views being worked on (zoomed to their crop), hide the Properties palette and the Project Browser so the user can watch, show the status window. Use when the user says bắt đầu việc, bắt đầu làm việc, vào việc, mở view đang làm, start work."
---

# Start work (bắt đầu việc)

No model change. Load `drafting-session` first if it is not loaded yet.

## Steps

1. **Re-anchor**: `hello_revit` → the document and the active view. Never reuse ids from an earlier turn.
2. **Find the views to work on**, in this order:
   1. the views or sheets named in the user's message;
   2. the current round's `TASKS.md` (`review/<date>_<code>/`);
   3. the views in the latest entry of `log/YYYY-MM.md`.

   Resolve the names to ids with `sheet_browser_names` / `sheet_views_ids`. Unclear or more than ~8 views → show the list and ask which ones.
3. **Hide the panes**:
   ```
   ui_view_focus {action:"panes", panes:"hide"}
   ```
   Note what it reports as `was shown / was hidden` for each pane in the session log entry (`UI: Properties was …, Project Browser was …`). `drafting-end` restores exactly that.
4. **Open the views**, the one to work on first opened last (it stays active):
   ```
   ui_view_focus {action:"open", viewId}
   ```
   Reply `view window not open yet: call again` → call it once more for that view (the zoom needs the window).
5. **Status window** (if the project uses it): `work_status {action:"show", title:<task>, message:"Bắt đầu"}`.

## Report

One short table: view (exact Project Browser name) → sheet → opened. Then one line: panes hidden.

## Never

- Close or rearrange the user's own view windows.
- Use `toggle` for the panes (it flips blindly); use `panes` with `hide` / `show`.
- Change anything in the model.
