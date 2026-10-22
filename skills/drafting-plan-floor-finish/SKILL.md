---
name: drafting-plan-floor-finish
description: "Develop floor finish plan drawings (khai triển mặt bằng hoàn thiện sàn) via Revit MCP, one step card at a time. Sibling of drafting-plan-dims. Cards: floor spot elevations (FL1–FL3: one per room, stair enclosures inside + outside), slope marks (SL1–SL5: spot slope, direction down, on roofs, ramps, sloped floors). Use for khai triển mặt bằng hoàn thiện sàn, mặt bằng hoàn thiện, hoàn thiện sàn, F tag sàn, tag cao độ sàn, cao độ phòng, ký hiệu độ dốc, spot slope, floor finish plan, floor finish marks."
---

# Floor finish plan (khai triển mặt bằng hoàn thiện sàn)

**Status: started.** Cards so far: floor spot elevations, slope marks; the finish mark cards are added when their rules are decided with the user.

**Tools** (load all in one ToolSearch call, when a card needs them): `annot_place`, `annotation_overlaps`, `delete_element`, `export_sheet_images`, `finish_mark_place`, `finish_marks_snap`, `finish_marks_survey`, `floor_levels_probe`, `mark_types`, `query_elements_with_filter`, `room_spots_missing`, `room_tags_missing`, `slope_spots_place`, `stair_door_spots`, `ui_view_focus`, `view_elem_boxes`.

Standard: `~/.claude/drafting-domain/drafting-plan-floor-finish.md` (Vietnamese). Sibling skill: `drafting-plan-dims`, which covers the same floor plan views.

Load `drafting-session` first. Take these values from the project `drafting-profile.md` (`templates/drafting-profile.md`):
- the floor finish tag type / mark family, when the project fixes one (open point: the profile has no row for it yet);
- the mark prefix for floor finishes, e.g. `F..` (open point, ask the user if not in the profile);
- the spot elevation type, the small-room area threshold, the stair room / view name parts and the ground-exit symbol (card 00, `drafting-annotation.md` §2b);
- the spot slope type, when the project fixes one (card 01, §2c SL2–SL4).

Scope: floor plan views of the building, on the sheets of the job. Not in scope:
- stair core plans: floor finish marks there are SB5 in `drafting-stair-plan`;
- ceiling plans: `drafting-ceiling-plan`;
- stair sections: `drafting-stair-section-parallel` / `drafting-stair-section-cross` (XB4 / LB4).

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-floor-spots.md` | Floor spot elevations (tag cao độ sàn), same as `drafting-plan-dims` card 03 |
| 01 | `steps/01-slope-marks.md` | Slope marks (ký hiệu độ dốc), same as `drafting-plan-dims` card 05 |

A card says: what to do (commands), **done when**, **skip only if**, never. New steps of this skill are added as new cards.

## Never

- Edit a room, wall or floor (R1).
- Choose the finish mark code yourself on a new job: take it from the room's finish parameter, the sheet legend, or ask the user (same rule as SB5 in `drafting-stair-plan`).
- Move, retype or delete finish marks placed by other drafters.
