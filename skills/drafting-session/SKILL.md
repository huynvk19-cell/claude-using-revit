---
name: drafting-session
description: "Ground rules + start/end routine for any Revit drafting work through Revit MCP (dims, tags, titles, viewports, crops, templates). Use at the start of every Revit drafting task, when resuming after a break or context summary, or when the user says tiếp tục, làm tiếp, bắt đầu, triển khai bản vẽ, sửa bản vẽ."
---

# Drafting session

Read first (only these two):
- `~/.claude/drafting-domain/drafting-work-rules.md` (Vietnamese).
- The project's `drafting-profile.md`, found in the project root or its `domain/`.
  - If it is missing, copy `~/.claude/drafting-domain/templates/drafting-profile.md` into the project.
  - Ask the user once for the blanks.

## Load only what the task needs

Do **not** read the whole `drafting-domain/` folder or every skill. Pick the task's row, load that skill, and let it open its one standard file.

| Task | Skill | Standard (`~/.claude/drafting-domain/`) |
|---|---|---|
| Start / end of a work session | `drafting-start` / `drafting-end` | — |
| A round of comments (PDF, TASKS.md) | `drafting-review-round` | per task, the row below |
| Grid dims | `drafting-grid-dims` | `drafting-grid-dims.md` |
| Level dims, dim types | `drafting-dims` | `drafting-dimensions.md` |
| Door/window dims (elevation, section) | `drafting-opening-dims` | `drafting-opening-dims.md` |
| Door/window tags (elevation, section) | `drafting-opening-tags` | `drafting-opening-tags.md` |
| Stair core **plan** | `drafting-stair-plan` | `drafting-stair-plan.md` |
| Stair **section along the flights** (parallel to the stair path) | `drafting-stair-section-parallel` | `drafting-stair-section-parallel.md` |
| Stair section **across** the flights (not parallel: flights seen end-on) | `drafting-stair-section-cross` | `drafting-stair-section-cross.md` |
| Room tags, view titles | `drafting-tags-titles` | `drafting-annotation.md` |
| Viewports, crops, templates, links, drawing list + print set | `drafting-views-sheets` | `drafting-views-sheets.md` |
| Checking by image | `drafting-visual-check` | — |

- A task touching two topics → load the two rows, nothing else.
- `drafting-tools.md` and `drafting-api-pitfalls.md` are look-up lists: search them for the one command or symptom you need (`grep`). Do not read them whole.
- The `related` list in a standard's header is for maintainers. Do not open those files because of it.
- A command's parameters: read the header comment of that one `.cs` file only when unsure.

## Hard rules (recite before acting)

1. Annotation, 2D and view settings only. Never edit walls, doors, rooms, 3D grid/level extents or links.
2. No sync unless asked this turn. Never touch the models the profile marks off-limits.
3. Elevation/section commands: ONE view per call, with `timeoutSeconds`.
4. "dừng" / "tạm dừng" → stop immediately and report where you stopped.
5. No "Claude"/"AI" in anything written to the model.
6. Hide via View Template, not Hide in View.
7. Never "Replace with text" on a dim: the value stays live; extra text goes in Prefix / Suffix / Above / Below (`dims_text`).

## Start

1. Re-anchor: `hello_revit` or `get_active_view`. Never reuse ids from an earlier turn or a summary without checking.
2. Open the working views, hide Properties and Project Browser, show the status window: follow `drafting-start`. Before each further view: `ui_view_focus {action:"open", viewId}`.
3. Find the task source (comment PDF, TASKS.md or the user's message) and the latest log in `review/<date>_<code>/`.

## During

- Preview first, then apply with `logPath`.
- New dims use the profile's **check** dim type.
- After each view: export an image **of the view** (`export_sheet_images {sheetNumbers:[], viewIds:[id]}`) and check it (`drafting-visual-check`). No computer-use screenshots unless the user asks.
- Give the user a one-line progress update when they have not heard from you for a while.

## Done = ĐỦ – ĐÚNG – ĐẸP (`drafting-work-rules.md` §2b)

Before calling a view finished, check all three:
- **Đủ (complete)**: every item of the topic's standard is on the view; the topic audit shows no `missing` except items the user agreed to drop. Never skip an item silently ("the project does not use it" needs proof from the project's own finished views); otherwise ask or list it under Việc tồn with the reason.
- **Đúng (correct)**: numbers from the model, references on real geometry at the edge the standard names, values match, every new dim/tag is actually drawn (`view_elem_boxes` Box ≠ null, seen on the image).
- **Đẹp (neat)**: `annotation_overlaps` = 0, dim lines evenly spaced and aligned, short-segment texts moved apart, short leaders, same look as the finished views on the same sheet.

## End of turn

1. Write a log entry to `log/YYYY-MM.md`.
2. Add new lessons:
   - general → the matching domain file;
   - project-only → the profile.
3. Close the status window. The panes stay hidden between turns; when the user ends the work session (kết thúc làm việc), follow `drafting-end`, which brings them back.
4. Report:
   - exact Revit names (sheet no. – name, view, View Template);
   - a table of what was done;
   - end with "Cần xem" and "Việc tồn".
