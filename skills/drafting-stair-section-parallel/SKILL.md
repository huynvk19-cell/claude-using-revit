---
name: drafting-stair-section-parallel
description: "Detail a stair SECTION cut parallel to the stair path (longitudinal, along the flights; NOT a plan, NOT a cross-section) via Revit MCP, rules LA–LC: flight heights (EQUAL RISERS), levels, flight goings, clear height under landings, handrail height; run, railing, F../C.. tags, spot elevations; riser numbers. Use for mặt cắt thang, mặt cắt dọc thang, mặt cắt cầu thang, dim chiều cao vế, cổ bậc, tag vế trên mặt cắt, stair section."
---

# Stair section parallel to the stair path (LA–LC)

**Tools** (load all in one ToolSearch call): `annot_copy_in_view`, `annot_place`, `annotation_overlaps`, `dims_text`, `dims_text_move`, `elevation_opening_tags`, `export_sheet_images`, `finish_mark_place`, `finish_marks_snap`, `level_dims_add`, `rail_top_at`, `stair_section_annotate`, `stair_section_info`, `tag_leaders_info`, `view_dims_snapshot`, `view_elem_boxes`.

**Section views cut along the flights only.** Plans → `drafting-stair-plan`. A section cut across the flights (flights seen end-on) → `drafting-stair-section-cross`.

Standard: `~/.claude/drafting-domain/drafting-stair-section-parallel.md` (Vietnamese). Its rules and its "Mẫu tham chiếu" decide every case. Read it first. Open `drafting-opening-tags.md` only if the section shows doors or windows (LB6).

Load `drafting-session` first. From the project `drafting-profile.md` take the **check** dim type, and whether LA4 carries `CLEAR` (default: no, as the sample).

Section views: **one view per call**, with `timeoutSeconds`.

**Checking a card**: `stair_section_info {viewId, rules:["<the card's rule>"]}` (e.g. `["LA5"]`) returns only that rule's rows and a count per rule. The full result (no `rules`) is for the survey card, written to `outPath`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**; `_annotate-common.md` once per job, before the first card that names it. In a workflow (`drafting-workflow`), each card runs on every section of its part before the next card.

| # | Card | Rule |
|---|---|---|
| 00 | `steps/00-survey.md` | survey (`stair_section_info`) |
| 01 | `steps/01-lb1-run-tags.md` | LB1 run tags (cut flights) |
| 02 | `steps/02-la1-flight-heights.md` | LA1 flight heights |
| 03 | `steps/03-la2-levels.md` | LA2 levels |
| 04 | `steps/04-la3-flight-goings.md` | LA3 flight goings |
| 05 | `steps/05-la4-clear-height.md` | LA4 clear height under landings |
| 06 | `steps/06-la5-handrail-height.md` | LA5 handrail height ★ |
| 07 | `steps/07-la6-walls-to-grids.md` | LA6 walls, doors → grids |
| 08 | `steps/08-lc-riser-numbers.md` | LC riser numbers |
| 09 | `steps/09-lb2-railing-tags.md` | LB2 railing tags |
| 10 | `steps/10-lb3-spot-elevations.md` | LB3 spot elevations |
| 11 | `steps/11-lb4-floor-finish.md` | LB4 floor finish F.. |
| 12 | `steps/12-lb5-soffit-finish.md` | LB5 soffit finish C.. |
| 13 | `steps/13-lb6-door-tags.md` | LB6 doors, windows |
| 14 | `steps/14-tidy.md` | tidy |
| 15 | `steps/15-recheck.md` | recheck, report |
| — | `steps/_annotate-common.md` | `stair_section_annotate`, formula text, manual references |

★ mandatory, never skipped. A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Count risers or treads from the drawing.
- Replace a dim value with text (use prefix / below).
- Add a stair path to a section.
- Put LA1 and LA2 on the same side, or dim one chain on both sides.
- Number the risers of a flight seen beyond.
- Report a section done while a cut landing has no LA5 rail-height dim.
- Edit stairs, railings, floors or walls (R1).
