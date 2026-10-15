---
name: drafting-stair-section-cross
description: "Detail a stair SECTION cut ACROSS the flights (flights seen end-on; NOT a plan, NOT a longitudinal section) via Revit MCP, rules XA–XC: flight-band heights (EQUAL RISERS), levels, clear-height | landing chain, widths to handrails and walls; run, handrail, F../C.. tags, spot elevations; riser numbers Start and End. Use for mặt cắt ngang thang, mặt cắt thang không song song, cắt ngang vế thang, stair cross-section."
---

# Stair section across the flights (XA–XC)

**Tools** (load all in one ToolSearch call): `annot_copy_in_view`, `annot_place`, `annotation_overlaps`, `dims_copy_refs`, `dims_edit`, `dims_text`, `elevation_opening_tags`, `export_sheet_images`, `finish_mark_place`, `finish_marks_snap`, `level_dims_add`, `rail_top_at`, `stair_numbers_match`, `stair_xsection_info`, `tag_leaders_info`, `tags_retype`, `view_dims_snapshot`, `view_elem_boxes`.

**Section views whose cut is NOT parallel to the stair path** (flights seen end-on). Along the flights → `drafting-stair-section-parallel`; plans → `drafting-stair-plan`.

Standard: `~/.claude/drafting-domain/drafting-stair-section-cross.md` (Vietnamese). Its rules and its "Mẫu tham chiếu" decide every case. Read it first. Open `drafting-opening-tags.md` only if the section shows doors or windows (XB6).

Load `drafting-session` first. From the project `drafting-profile.md` take the **check** dim type, the run tag types per floor block, the finish-mark code parameter and the numbers' source (a finished cross-section of the project, if any).

Section views: **one view per call**, with `timeoutSeconds`.

**Checking a card**: `stair_xsection_info {viewId, rules:["<the card's rule>"]}` (e.g. `["XA1"]`) returns only that rule's rows and a count per rule. The full result (no `rules`) is for the survey card, written to `outPath`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**; the `_…-common.md` notes once per job, before the first card that names them. In a workflow (`drafting-workflow`), each card runs on every cross-section before the next card.

| # | Card | Rule |
|---|---|---|
| 00 | `steps/00-survey.md` | survey (`stair_xsection_info`) |
| 01 | `steps/01-xb1-run-tags.md` | XB1 run tags (both halves) |
| 02 | `steps/02-xa1-band-heights.md` | XA1 band heights |
| 03 | `steps/03-xa2-levels.md` | XA2 levels |
| 04 | `steps/04-xa3-widths-to-handrails.md` | XA3 widths to handrails |
| 05 | `steps/05-xa4-clear-thickness.md` | XA4 clear height \| landing thickness |
| 06 | `steps/06-xa5-wall-to-wall.md` | XA5 wall to wall |
| 07 | `steps/07-xa6-handrail-height.md` | XA6 handrail height ★ |
| 08 | `steps/08-xc-riser-numbers.md` | XC riser numbers (Start and End) |
| 09 | `steps/09-xb2-handrail-tags.md` | XB2 handrail tags |
| 10 | `steps/10-xb3-spot-elevations.md` | XB3 spot elevations |
| 11 | `steps/11-xb4-floor-finish.md` | XB4 floor finish F.. |
| 12 | `steps/12-xb5-soffit-finish.md` | XB5 soffit finish C.. |
| 13 | `steps/13-xb6-door-tags.md` | XB6 doors |
| 14 | `steps/14-tidy.md` | tidy |
| 15 | `steps/15-recheck.md` | recheck, report |
| — | `steps/_dims-common.md` | shared by 02–07 |
| — | `steps/_tags-common.md` | shared by 01, 09, 10 |

★ mandatory, never skipped. A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Count risers from the drawing, or merge two bands in one formula segment.
- Replace a dim value with text (use prefix / below).
- Number the flights rising toward the viewer, or use Display Rule Odd here.
- Put XA1 and XA2 on the same side, or keep duplicate overall dims.
- Add a stair path.
- Edit stairs, railings, floors, walls or start numbers (R1).
