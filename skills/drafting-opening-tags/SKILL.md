---
name: drafting-opening-tags
description: "Tag every visible window and door (incl. roll-up doors) on elevation and section views: exactly one tag per visible opening, above + centred + touching with a short leader (large roll-up doors: tag inside the door), lifted or beside when blocked, never over dims/tags/hard elements, same height per row; fix duplicates/wrong tags. Use for tag cửa, tag cửa sổ, tag cửa đi, tag cửa cuốn, thiếu tag, tag trùng, tag đè dim, căn tag, door tags, window tags."
---

# Opening tags (elevations / sections)

**Tools** (load all in one ToolSearch call): `annotation_overlaps`, `door_window_tag_check`, `elevation_opening_tags`, `export_sheet_images`, `tag_align`.

Standard: `~/.claude/drafting-domain/drafting-opening-tags.md`. Rules T1–T8 decide every case. Read it first.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-audit.md` | Audit (khảo sát tag cửa) |
| 01 | `steps/01-place-tags.md` | Place tags (đặt tag) |
| 02 | `steps/02-duplicates-orphans.md` | Duplicates, orphans (tag trùng, tag mồ côi) |
| 03 | `steps/03-align-rows.md` | Align rows (căn tag theo hàng) |
| 04 | `steps/04-rollup-doors.md` | Roll-up doors (cửa cuốn) |
| 05 | `steps/05-recheck.md` | Recheck (kiểm lại) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Tag a hidden opening.
- Put a tag head on its own opening. The only exception is a large roll-up door (T5).
- Put a window tag beside its window.
- Batch several views in one call.
- Run `repositionAll` over hand-adjusted views.
- Undo restores everything: `mode:"undo"` with the `logPath`.
