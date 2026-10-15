# Workflow: reviewer comment round

One round of comments (PDF + TASKS.md/CSV in `review/<date>_<code>/`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 1 | `drafting-review-round` | steps 1–2 (read, classify) | once |
| 2 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): the views and sheets of **every** annotation task, sorted by topic and ViewType |
| 3 | `the topic's skill of each task (`drafting-session` table)` | survey card, the cards the comment is about, recheck card | the task's views |
| 4 | `drafting-tags-titles` | 01 | the sheets a task changed |
| 5 | `drafting-visual-check` | — | every view a task changed |
| 6 | `drafting-end` | — | once |

Tasks of the same topic run together under 3: each card in one pass over all their views, not task after task. In the checklist each task is still its own row under 3 (`3 · T01`, `3 · T02` …), so no comment is lost.
