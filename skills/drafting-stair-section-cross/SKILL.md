---
name: drafting-stair-section-cross
description: "Detail a stair SECTION cut ACROSS the flights (not parallel to the stair path: the flights are seen end-on; NOT a plan, NOT a longitudinal section) via Revit MCP, rules XA–XC: flight-band height chain '169.4mm x 16R = 2710 (EQUAL RISERS)' + overall, level chain through the plan-cut marks, one continuous clear-height | landing-thickness chain (2495 | 215 | 2325 …), width chain wall → handrail → handrail → wall per stair block (70 | 1745 | 70 | 320 …), wall-to-wall below; run tags on every seen flight (vertical text outside the wall of its half, horizontal leader), handrail tags, spot elevations, floor finish (F..) and soffit (C..) marks; riser numbers 'Start and End' only on flights rising away from the viewer. Use for mặt cắt ngang thang, mặt cắt thang không song song, cắt ngang vế thang, mặt cắt B-B thang, section no parallel, stair cross-section."
---

# Stair section across the flights (XA–XC)

**Section views whose cut is NOT parallel to the stair path** (flights seen end-on). Along the flights → `drafting-stair-section-parallel`; plans → `drafting-stair-plan`.

Standard: `~/.claude/drafting-domain/drafting-stair-section-cross.md` (Vietnamese). Its rules and its "Mẫu tham chiếu" decide every case. Read it first. Open `drafting-opening-tags.md` only if the section shows doors or windows (XB6).

Load `drafting-session` first. From the project `drafting-profile.md` take the **check** dim type, the run tag types per floor block, the finish-mark code parameter and the numbers' source (a finished cross-section of the project, if any).

Section views: **one view per call**, with `timeoutSeconds`.

## 1. Read the view (read-only)

```
stair_xsection_info {viewId, outPath:"<review>/xsec-<viewId>.json", excludeStairIds?}
```

- `Error` "every flight runs across the view" → it is a longitudinal section: switch to `drafting-stair-section-parallel`.
- `Flights`: F1… going up, `Half` (left / right), `Facing` (`away` = risers seen → numbers; `toward` = soffit seen → no numbers), `TagText`, `RiseText`, run tags with their text, `FirstNumber` vs `FirstByElevation`. **Never count risers from the drawing.**
- `Slabs`: landings and floors with top, soffit, thickness, the x range the section cuts, `Cut`.
- `Dims`: XA1 per band (OK / missing / Replace with text / wrong prefix or below), XA2, XA3 per stair block, XB3 spots.
- `ClearHeights`: per half, the XA4 chain expected from the model (clear | thickness …) and what is missing.
- `Issues`: merged-band segments, 0 mm segments, duplicate dims, wrong tag type (From/To EL off), numbers on the wrong flights, Display Rule, numbering not continuous.

Show the user one table: band → flights (half, facing) → risers × height → tag text → numbers; then the issues by rule. Also read the hand dims with `view_dims_snapshot` before treating a view as a sample.

Stop and ask when: no host stairs (link); `LC:` model mismatch; numbering not continuous (start numbers are a stair property: R1, the user decides); XB3 / XA6 still "chờ user chốt" in the standard.

## 2. Dims

No annotate command for cross-sections yet. Per rule:

1. **XA2**: `level_dims_add {viewId, side:<opposite of XA1>, typeName:<check type>, mode:"preview", logPath}` → `apply`. Skip when the view has a level chain.
2. **XA1, XA4, XA3, XA5**: references as the project's hand dims (`view_dims_snapshot` → `Refs`): landing / floor tops and soffits on the **Stairs** element and Floors (run / landing 3D faces are hidden: dims on them are not drawn), walls, **Handrails**, never a link. Options:
   - same stair already dimmed in another cross-section → `dims_copy_refs` (preview → apply);
   - otherwise ask the user for one hand dim per chain kind and rebuild from its references; check every new dim is drawn (`view_elem_boxes` Box ≠ null).
   - XA1: one segment per band (split merged segments: `dims_edit` with log, then rebuild); XA4: one continuous chain in the `away` half, values = `ClearHeights.Expected`.
3. **Text** (XA1): `dims_text {viewId, mode:"apply", logPath, items:[{dimId, valueMm, prefix:"169.4mm x 16R = ", below:"(EQUAL RISERS)"}]}`; a Replace-with-text value is cleared, never kept (R9). Take the text from `RiseText`.

## 3. Tags, marks, numbers

- **XB1** run tags: one per seen flight (both halves), vertical text outside the wall of its half, horizontal leader to the flight at mid band: `annot_place` (kind tag, the section run tag type of that floor block). Wrong From/To EL → `tags_retype`.
- **XB2** handrail tags (`P01` per seen handrail per band, `P02` on the balustrade): `annot_place`, horizontal leader, head inside the core next to the rail; skip a rail hidden behind a bigger one.
- **XB3** spots: `annot_place` (kind spot) on each cut landing / floor top (until the user decides otherwise).
- **XB4 / XB5** F.. / C..: copy from a landing of the same view (`annot_copy_in_view`) or `finish_mark_place`, then `finish_marks_snap {mode:"survey"}` → `preview` → `apply` (dots on the top / the soffit).
- **XB6** doors: as LB6 (`elevation_opening_tags`, then `annot_place` for doors in cut walls).
- **XC** numbers only on `away` flights: create on the run (NumberSystem) and copy the settings of a finished cross-section number with `stair_numbers_match` (Display Rule **Start and End**, Tag Type Riser, Right Quarter, Justify Back). Remove numbers from `toward` flights.

## 4. Verify and report

1. `stair_xsection_info` again → XA1 rows `OK`, `ClearHeights` complete on one half, `Issues` empty except those the user accepted.
2. `finish_marks_snap {mode:"survey"}`, `annotation_overlaps {viewId}`.
3. Export the **view** image and compare with the standard's sample (`drafting-visual-check`).
4. Report per `drafting-session`: exact view name; table per band (rise dim, tags, numbers); **Cần xem** (XB3 / XA6 decisions, numbering vs plan); **Việc tồn**.

## Never

- Count risers from the drawing, or merge two bands in one formula segment.
- Replace a dim value with text (use prefix / below).
- Number the flights rising toward the viewer, or use Display Rule Odd here.
- Put XA1 and XA2 on the same side, or keep duplicate overall dims.
- Add a stair path.
- Edit stairs, railings, floors, walls or start numbers (R1).
