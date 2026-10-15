---
name: drafting-stair-plan
description: "Detail a stair core PLAN view (plan only, NOT a stair section) via Revit MCP, rules SA1–SD: clear widths, run-length dims with formula (EQUAL TREADS), landings, wall/door chains to grids; run, railing, finish tags, spot elevations; tread counts and riser numbers; stair path arrow. Use for mặt bằng thang, mặt bằng lõi thang, thông thuỷ vế thang, CLEAR, chiếu nghỉ, vế thang, đếm bậc, đánh số bậc, tag vế, tay vịn, stair path, stair core plan. Not for mặt cắt thang."
---

# Stair core plan (SA1–SD)

**Tools** (load all in one ToolSearch call): `annot_place`, `annotation_overlaps`, `detail_lines_set`, `dim_stable_refs`, `dims_at_positions`, `dims_copy_refs`, `dims_edit`, `dims_rail_refs`, `dims_text`, `dims_text_move`, `export_sheet_images`, `number_systems_copy`, `stair_gap_x`, `stair_plan_annotate`, `stair_plan_audit`, `tag_leaders_info`, `view_elem_boxes`, `view_range_info`.

**Plan views only.** Stair sections are a different job: along the flights → `drafting-stair-section-parallel`; across the flights → `drafting-stair-section-cross`. If the view is a section or elevation, stop and say so.

Standard: `~/.claude/drafting-domain/drafting-stair-plan.md` (Vietnamese). Its rules decide every case. Read it first. Its section "Mẫu tham chiếu" describes the user's sample sheet: match that layout.

Load `drafting-session` first. From the project `drafting-profile.md` take:
- the **check** dim type → new dims;
- stair core views (name filter) and whether tread numbers are shown (default: yes);
- tag / path type overrides, if the profile names any. Otherwise the project's most-used type per category.

Plan views only. Tools have no project defaults.

- **All plans of a step in one call**: `stair_plan_audit`, `stair_plan_annotate`, `stair_gap_x` take `viewIds` (logs and outPaths get `-<viewId>`; undo with the same `viewIds` + `logPath`). The other commands: one view per call.
- **Checking a card**: `stair_plan_audit {viewIds, rules:[<the card's rule>]}` returns only that rule's rows and a count per rule (a few hundred tokens). The full result (no `rules`) is for the survey card, written to `outPath`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**; the two `_…-common.md` notes once per job, before the first card that names them. In a workflow (`drafting-workflow`), each card runs on every plan view before the next card.

| # | Card | Rule |
|---|---|---|
| 00 | `steps/00-survey.md` | survey (`stair_plan_audit`) |
| 01 | `steps/01-sb1-run-tags.md` | SB1 run tags |
| 02 | `steps/02-sa1-clear-width.md` | SA1 clear width of runs |
| 03 | `steps/03-sa3-landings.md` | SA3 landings |
| 04 | `steps/04-sa2-run-lengths.md` | SA2 run lengths |
| 05 | `steps/05-sa4-walls-to-grids.md` | SA4 walls, doors → grids |
| 06 | `steps/06-c-tread-numbers.md` | C tread numbers |
| 07 | `steps/07-sd-stair-path.md` | SD stair path |
| 08 | `steps/08-sb2-railing-tags.md` | SB2 railing tags |
| 09 | `steps/09-sb3-spot-elevations.md` | SB3 spot elevations |
| 10 | `steps/10-sb5-floor-finish.md` | SB5 floor finish |
| 11 | `steps/11-sb6-wall-finish.md` | SB6 wall finish |
| 12 | `steps/12-sb4-door-tags.md` | SB4 doors, windows |
| 13 | `steps/13-open-gap-x.md` | X over the open gap |
| 14 | `steps/14-tidy.md` | tidy |
| 15 | `steps/15-recheck.md` | recheck, report |
| — | `steps/_dims-common.md` | shared by 02–05 |
| — | `steps/_tags-common.md` | shared by 08–12 |

A card says: what to do (commands), **done when**, **skip only if**, what to watch out for.

## Never

- Count treads by eye or from the visible lines.
- Replace a dim value with text (use prefix/suffix).
- Use a tag, spot or path type the project does not already have without asking.
- Show UP/DOWN text on a stair path.
- Put a run tag under a dim, or a railing tag head on the run lines.
- Mix formula spellings (`280mm x 14T = 3920` only).
- Count the top tread that sits at the landing level.
- Put `CLEAR` on anything but the clear width of a run (not on landings, overalls, handrails, walls).
- Measure a clear width across an opening (shaft) or to a wall when a railing / step edge comes first.
- Skip an item of the standard silently (SB5, SB6!). Done means ĐỦ – ĐÚNG – ĐẸP (`drafting-session`).
- Dim the same length on both sides of the core.
- Edit stairs, railings, walls or floors (R1). Exception, with the user's permission: the Stairs "Tread/Riser Start Number" for continuous numbering.
- Set a length parameter with `modify_element_parameter` (unit parsing: feet).
- Draw a diagonal leader.
- Take the SA3 end from the audit without checking the drawn handrail end on the image.
- Annotate with fewer runs / landings than the view shows.
