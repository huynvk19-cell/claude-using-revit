# Workflow: stair core

All views of one stair core. Each kind of view is its own batch: plans, sections along the flights, sections across the flights (sections of both kinds share a ViewType but are different topics).

| # | Step (skill) | Applies to | Done when | Skip only if |
|---|---|---|---|---|
| 1 | `drafting-start` | once | batches formed and shown (plans / along / across), views open, snapshot of every batch written | — |
| 2 | `drafting-stair-plan` | plan batches | SA1–SD: `stair_plan_audit` has no `missing`; ĐỦ – ĐÚNG – ĐẸP met | the core has no plan views in the job |
| 3 | `drafting-stair-section-parallel` | along batches | LA–LC: every item of the standard on the view; ĐỦ – ĐÚNG – ĐẸP met | no section is cut along the flights |
| 4 | `drafting-stair-section-cross` | across batches | XA–XC: every item of the standard on the view; ĐỦ – ĐÚNG – ĐẸP met | no section is cut across the flights |
| 5 | `drafting-visual-check` | each batch | checker finds no fault, or each fault is under Cần xem | — |
| 6 | `drafting-end` | once | last mark applied, log written, panes back, report with the checklist | — |

Why this order: plans first, because the sections repeat their run names, tread counts and levels (2→3, 4). Stair-view titles stay as they are unless a comment asks (`drafting-tags-titles`).
