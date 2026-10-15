# Workflow: stair core

All views of one stair core, gathered once at the start, in three parts by view kind: **A plans**, **B sections along the flights**, **C sections across the flights**. Each step is one item of the standard and runs on **every view of its part** before the next step starts.

Per part, load its skill and its one standard when the part starts (A → `drafting-stair-plan`, B → `drafting-stair-section-parallel`, C → `drafting-stair-section-cross`). Each step is a **step card** of that skill: open only the card of the step at hand; it holds what to do, **done when** and **skip only if**. The skill's "Never" list still applies.

## Start (once)

| # | Step | Done when |
|---|---|---|
| 0 | `drafting-start`: gather every plan and section of the core, sort into parts A / B / C, open, snapshot | the checklist lists every view under its part; snapshot of every batch written |

## A. Plans (`drafting-stair-plan`, rules SA–SD)

Each step is a **step card** of the skill: `~/.claude/skills/drafting-stair-plan/steps/<card>`. The card holds what to do, **done when** and **skip only if**.

| # | Item | Card |
|---|---|---|
| A0 | Survey | `00-survey.md` |
| A1 | SB1 run tags | `01-sb1-run-tags.md` |
| A2 | SA1 clear width of runs | `02-sa1-clear-width.md` |
| A3 | SA3 landings | `03-sa3-landings.md` |
| A4 | SA2 run lengths | `04-sa2-run-lengths.md` |
| A5 | SA4 walls, doors, windows → grids | `05-sa4-walls-to-grids.md` |
| A6 | C tread numbers | `06-c-tread-numbers.md` |
| A7 | SD stair path | `07-sd-stair-path.md` |
| A8 | SB2 railing tags | `08-sb2-railing-tags.md` |
| A9 | SB3 spot elevations | `09-sb3-spot-elevations.md` |
| A10 | SB5 floor finish `F..` ★ | `10-sb5-floor-finish.md` |
| A11 | SB6 wall finish `W..` ★ | `11-sb6-wall-finish.md` |
| A12 | SB4 doors, windows | `12-sb4-door-tags.md` |
| A13 | X over the open gap between flights | `13-open-gap-x.md` |
| A14 | Tidy | `14-tidy.md` |
| A15 | Recheck | `15-recheck.md` |

★ mandatory, never skipped.

## B. Sections along the flights (`drafting-stair-section-parallel`)

One view per call, with `timeoutSeconds`. Cards: `~/.claude/skills/drafting-stair-section-parallel/steps/<card>`.

| # | Item | Card |
|---|---|---|
| B0 | Survey | `00-survey.md` |
| B1 | LB1 run tags (cut flights) | `01-lb1-run-tags.md` |
| B2 | LA1 flight heights | `02-la1-flight-heights.md` |
| B3 | LA2 levels | `03-la2-levels.md` |
| B4 | LA3 flight goings | `04-la3-flight-goings.md` |
| B5 | LA4 clear height under landings | `05-la4-clear-height.md` |
| B6 | LA5 handrail height ★ | `06-la5-handrail-height.md` |
| B7 | LA6 walls, doors → grids | `07-la6-walls-to-grids.md` |
| B8 | LC riser numbers | `08-lc-riser-numbers.md` |
| B9 | LB2 railing tags | `09-lb2-railing-tags.md` |
| B10 | LB3 spot elevations | `10-lb3-spot-elevations.md` |
| B11 | LB4 floor finish `F..` | `11-lb4-floor-finish.md` |
| B12 | LB5 soffit finish `C..` | `12-lb5-soffit-finish.md` |
| B13 | LB6 doors, windows | `13-lb6-door-tags.md` |
| B14 | Tidy | `14-tidy.md` |
| B15 | Recheck | `15-recheck.md` |

★ mandatory, never skipped.

## C. Sections across the flights (`drafting-stair-section-cross`)

One view per call, with `timeoutSeconds`. Cards: `~/.claude/skills/drafting-stair-section-cross/steps/<card>`.

| # | Item | Card |
|---|---|---|
| C0 | Survey | `00-survey.md` |
| C1 | XB1 run tags (both halves) | `01-xb1-run-tags.md` |
| C2 | XA1 band heights | `02-xa1-band-heights.md` |
| C3 | XA2 levels | `03-xa2-levels.md` |
| C4 | XA3 widths to handrails | `04-xa3-widths-to-handrails.md` |
| C5 | XA4 clear height \| landing thickness | `05-xa4-clear-thickness.md` |
| C6 | XA5 wall to wall | `06-xa5-wall-to-wall.md` |
| C7 | XA6 handrail height ★ | `07-xa6-handrail-height.md` |
| C8 | XC riser numbers | `08-xc-riser-numbers.md` |
| C9 | XB2 handrail tags | `09-xb2-handrail-tags.md` |
| C10 | XB3 spot elevations | `10-xb3-spot-elevations.md` |
| C11 | XB4 floor finish `F..` | `11-xb4-floor-finish.md` |
| C12 | XB5 soffit finish `C..` | `12-xb5-soffit-finish.md` |
| C13 | XB6 doors | `13-xb6-door-tags.md` |
| C14 | Tidy | `14-tidy.md` |
| C15 | Recheck | `15-recheck.md` |

★ mandatory, never skipped.

## End (once)

| # | Step | Done when |
|---|---|---|
| D | `drafting-visual-check` on every view of the job | checker finds no fault, or each fault is under Cần xem |
| E | `drafting-end` | last mark applied, log written, panes back, report with the full checklist |

Why this order: plans before sections, because the sections repeat the plans' run names, riser counts, numbers and levels. Inside a part: survey first (every number comes from the model); run tags before the outer dims (SA2 sits outside the SB1 tags); dims before the other tags and marks, which must avoid them; tidy and recheck last. Stair-view titles stay as they are unless a comment asks.
