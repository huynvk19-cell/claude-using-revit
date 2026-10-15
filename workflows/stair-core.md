# Workflow: stair core

All views of one stair core, gathered once at the start, in three parts by view kind: **A plans**, **B sections along the flights**, **C sections across the flights**. Each step is one item of the standard and runs on **every view of its part** before the next step starts.

Per part, load its skill and its one standard when the part starts (A → `drafting-stair-plan`, B → `drafting-stair-section-parallel`, C → `drafting-stair-section-cross`). "How" points to the command or the skill section; the skill's rules and "Never" list still apply.

Every dim step: fix the text of existing dims of that item first (`dims_text`, prefix/suffix/below, never Replace with text), then add what is missing in the check type; every new dim drawn (`view_elem_boxes` Box ≠ null).

## Start (once)

| # | Step | Done when |
|---|---|---|
| 0 | `drafting-start`: gather every plan and section of the core, sort into parts A / B / C, open, snapshot | the checklist lists every view under its part; snapshot of every batch written |

## A. Plans (`drafting-stair-plan`, rules SA–SD)

| # | Item | How | Done when | Skip only if |
|---|---|---|---|---|
| A0 | Survey | `stair_plan_audit` (skill §1), table per view | every view audited; run / landing count matches what the view shows; every stop-and-ask case asked | — |
| A1 | SB1 run tags | `stair_plan_annotate parts:["runTags"]` (§3) | one tag per seen run, `From EL … To EL …` = the run's real elevations, outside the side wall, leader into the seen part | — |
| A2 | SA1 clear width of runs | `dims_rail_refs` / `dims_at_positions` (§4) | audit SA1 rows `OK` at both ends of the runs; `CLEAR` only on run clear widths; overall chain `handrail \| overall \| handrail \| gap` closed on the walls | — |
| A3 | SA3 landings | as A2 (§4) | audit SA3 `OK` on the mid landing and the floor landing; end on the handrail end **seen on the image** | — |
| A4 | SA2 run lengths | `dims_at_positions` (§4) | audit SA2 `OK`: chain `landing \| 280mm x 14T = … (EQUAL TREADS) \| landing \| (grid)` + overall, outside the SB1 tags, one side only | — |
| A5 | SA4 walls, doors, windows → grids | `dims_at_positions` (§4) | audit SA4 `OK`: grid → wall faces → opening edges → grid + overall, outside the end walls | — |
| A6 | C tread numbers | `stair_plan_annotate parts:["numbers"]`, `number_systems_copy` (§3) | every seen run numbered; continuous from the lowest riser of the building; odd numbers shown; V1 / V3 columns on opposite sides | the profile says tread numbers are not shown |
| A7 | SD stair path | `stair_plan_annotate parts:["path"]` (§3) | one Fixed Up path per seen stair, no UP / DOWN, arrows point up (image) | — |
| A8 | SB2 railing tags | `annot_place` (§5) | `P01` on each wall handrail, `P02` on the centre railing; heads off the tread lines; orthogonal leaders | — |
| A9 | SB3 spot elevations | `annot_place` kind spot on the **Stairs** (§5) | one on every seen landing, one on the floor landing in the core; the plan cuts the stair door → also one on the floor **outside** the door, even when it is the same level (inside vs outside the enclosure on the escape route) | — |
| A10 | SB5 floor finish `F..` | finish mark (§5) | one per landing and floor landing, next to its spot; code from the room / legend / finished views, unknown → asked | **never** (mandatory) |
| A11 | SB6 wall finish `W..` | finish mark (§5) | at least one per wall finish kind seen; code from the room / legend / finished views, unknown → asked | **never** (mandatory) |
| A12 | SB4 doors, windows | `annot_place` (§5) | exactly one tag per drawn opening, off the SA4 dims and the swing | the view draws no door or window |
| A13 | Open gap between flights (thông tầng) | `detail_lines_set` for an existing X; no command creates one yet → manual (§5) | a gap open to the floors below (well between two flights, used for fire-fighting pipes) carries the X in `<Overhead>` dashed lines, fitting its 4 corners; no tag head on it | the side of the flight is a solid wall or a floor |
| A14 | Tidy | `annotation_overlaps`, `dims_text_move`, `tag_leaders_info` | overlaps = 0; short texts apart; no diagonal leader (shape `D`) | — |
| A15 | Recheck | `stair_plan_audit` again (§6) | no `missing`, no `Issues` except those the user accepted; ĐỦ – ĐÚNG – ĐẸP | — |

## B. Sections along the flights (`drafting-stair-section-parallel`, rules LA–LC)

One view per call, with `timeoutSeconds`.

| # | Item | How | Done when | Skip only if |
|---|---|---|---|---|
| B0 | Survey | `stair_section_info` (skill §1), table per view | every view read; flights cut / beyond known; stop-and-ask cases asked | — |
| B1 | LB1 run tags | `stair_section_annotate parts:["LB1"]` (§2) | one tag per **cut** flight, text `From EL … To EL … / …R` right; none on flights beyond | — |
| B2 | LA1 flight heights | `parts:["LA1"]` (§2) | one segment per flight `169.4mm x 16R = 2710 (EQUAL RISERS)`, outside one wall | — |
| B3 | LA2 levels | `level_dims_add` (§2) | level chain through the plan-cut markers + overall, on the side opposite LA1 | the view already has the level chain |
| B4 | LA3 flight goings | `parts:["LA3"]` (§2) | one chain per cut flight `landing \| 280mm x 15T = … (EQUAL TREADS) \| landing`; overall wall to wall once | — |
| B5 | LA4 clear height under landings | `parts:["LA4"]` (§2) | one column per side, landing top → soffit above, every landing | — |
| B6 | LA5 handrail height | `parts:["LA5"]` (§2) | `info` shows a rail dim on **every cut landing** (1200), + 900 where P01 is seen along the landing | **never** (mandatory) |
| B7 | LA6 walls, doors → grids | manual references (§2 table) | chain + overall below the section; a second one on top when the upper walls differ | — |
| B8 | LC riser numbers | `parts:["LC"]` (§2) | cut flights only; continuous; same number as the plan at the same riser | — |
| B9 | LB2 railing tags | `annot_place` (§2) | one per cut flight for the seen railing; `P01` on landings where it is seen; hidden rails untagged | — |
| B10 | LB3 spot elevations | `parts:["LB3"]` (§2) | every seen landing / floor top, at each end | — |
| B11 | LB4 floor finish `F..` | finish mark (§3) | one per landing / floor with a spot, leader down to the finish top | — |
| B12 | LB5 soffit finish `C..` | finish mark (§3) | one per seen landing soffit, same column as its `F..` | — |
| B13 | LB6 doors, windows | `elevation_opening_tags` + `annot_place` (§3) | one tag per door seen **and** per door in a cut wall, always with a leader | the section shows no door or window |
| B14 | Tidy | `annotation_overlaps`, `dims_text_move` | overlaps = 0; formula texts off the riser numbers | — |
| B15 | Recheck | `stair_section_info` again (§4) | every `Dims` row `OK` (LA5 one row per cut landing), no `Issues` except accepted | — |

## C. Sections across the flights (`drafting-stair-section-cross`, rules XA–XC)

One view per call, with `timeoutSeconds`.

| # | Item | How | Done when | Skip only if |
|---|---|---|---|---|
| C0 | Survey | `stair_xsection_info` (skill §1), `view_dims_snapshot` | every view read; bands, halves, facing known; stop-and-ask cases asked | — |
| C1 | XB1 run tags | `annot_place`, `tags_retype` (§3) | one per seen flight, **both halves**, vertical text outside the wall of its half, From/To EL right | — |
| C2 | XA1 band heights | `dims_copy_refs` / hand-dim refs, `dims_text` (§2) | one segment per band with `… R = … (EQUAL RISERS)`, no merged band, overall outermost | — |
| C3 | XA2 levels | `level_dims_add` (§2) | level chain on the side opposite XA1 | the view already has the level chain |
| C4 | XA3 widths to handrails | as C2 (§2) | one chain per stair block, walls → handrails → runs | — |
| C5 | XA4 clear height \| landing thickness | as C2 (§2) | one continuous chain in the `away` half = `ClearHeights.Expected` | — |
| C6 | XA5 wall to wall | as C2 (§2) | chain + overall, bottom (and top when the walls differ) | — |
| C7 | XA6 handrail height | as C2 | per the standard once the user has decided | the standard still says "chờ user chốt": mark `!`, ask once |
| C8 | XC riser numbers | NumberSystem + `stair_numbers_match` (§3) | `away` flights only, Start and End; none on `toward` flights; matches the plan | — |
| C9 | XB2 handrail tags | `annot_place` (§3) | `P01` per seen handrail per band, `P02` on the balustrade, horizontal leaders | — |
| C10 | XB3 spot elevations | `annot_place` kind spot (§3) | every cut landing / floor top (until the user decides otherwise) | — |
| C11 | XB4 floor finish `F..` | `annot_copy_in_view` / `finish_mark_place`, `finish_marks_snap` (§3) | one per cut landing / floor top, dot on the top | — |
| C12 | XB5 soffit finish `C..` | as C11 | one per cut landing soffit, same column as its `F..`, dot on the soffit | — |
| C13 | XB6 doors | as B13 | one tag per door, always with a leader | the section shows no door |
| C14 | Tidy | `annotation_overlaps`, `finish_marks_snap {mode:"survey"}` | overlaps = 0; every finish dot on its face | — |
| C15 | Recheck | `stair_xsection_info` again (§4) | XA1 `OK`, `ClearHeights` complete, no `Issues` except accepted | — |

## End (once)

| # | Step | Done when |
|---|---|---|
| D | `drafting-visual-check` on every view of the job | checker finds no fault, or each fault is under Cần xem |
| E | `drafting-end` | last mark applied, log written, panes back, report with the full checklist |

Why this order: plans before sections, because the sections repeat the plans' run names, riser counts, numbers and levels. Inside a part: survey first (every number comes from the model); run tags before the outer dims (SA2 sits outside the SB1 tags); dims before the other tags and marks, which must avoid them; tidy and recheck last. Stair-view titles stay as they are unless a comment asks.
