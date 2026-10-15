# Workflow: reviewer comment round

One round of comments (PDF + TASKS.md/CSV in `review/<date>_<code>/`). The batches are the round's tasks grouped by topic and view kind.

| # | Step (skill) | Applies to | Done when | Skip only if |
|---|---|---|---|---|
| 1 | `drafting-review-round` steps 1–2 (read + classify) | once | every task has id, sheet, view, topic and a class (annotation / model change / manual); the batches are formed from the annotation tasks | — |
| 2 | `drafting-start` | once | batch views open, snapshot of every batch written | — |
| 3 | `drafting-review-round` step 3 → the topic's skill (`drafting-session` table) | each batch | each task of the batch done to its topic's standard, logged in `Txx-<what>.json` | the user says the task is done |
| 4 | `drafting-tags-titles` (viewport titles) | sheets touched | `viewport_title_audit` clean on every sheet a task changed | no grid, dim or crop changed on a sheet |
| 5 | `drafting-visual-check` | each batch | checker finds no fault, or each fault is under Cần xem | — |
| 6 | `drafting-end` | once | log written, panes back, report: task → sheet/view → result, model-side and manual lists, changed sheets | — |

In the checklist, give each task its own row under step 3 (`3 · T01`, `3 · T02` …) so no comment is lost.
