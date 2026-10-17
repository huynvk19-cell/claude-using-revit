---
name: drafting-session
description: "Ground rules + start/end routine for any Revit drafting work through Revit MCP (dims, tags, titles, viewports, crops, templates): batches of up to 10 views of one kind, mark types on everything new. Use at the start of every Revit drafting task, when resuming after a break or context summary, or when the user says tiếp tục, làm tiếp, bắt đầu, triển khai bản vẽ, sửa bản vẽ, trả về type chính thức."
---

# Drafting session

**Tools** (load all in one ToolSearch call): `annotation_overlaps`, `dims_text`, `export_sheet_images`, `hello_revit`, `mark_types`, `ui_view_focus`, `view_elem_boxes`.

Read first (only these two):
- `~/.claude/drafting-domain/drafting-work-rules.md` (Vietnamese).
- The project's `drafting-profile.md`, found in the project root or its `domain/`.
  - If it is missing, copy `~/.claude/drafting-domain/templates/drafting-profile.md` into the project.
  - Ask the user once for the blanks.

## Load only what the task needs

Do **not** read the whole `drafting-domain/` folder or every skill. Pick the task's row, load that skill, and let it open its one standard file.

| Task | Skill | Standard (`~/.claude/drafting-domain/`) |
|---|---|---|
| A whole job, several topics in order (facade, plans, stair core, comment round) | `drafting-workflow` | its one file in `workflows/` |
| Start / end of a work session | `drafting-start` / `drafting-end` | — |
| A round of comments (PDF, TASKS.md) | `drafting-review-round` | per task, the row below |
| Grid dims | `drafting-grid-dims` | `drafting-grid-dims.md` |
| Level dims, dim types | `drafting-dims` | `drafting-dimensions.md` |
| Door/window dims (elevation, section; plans → `drafting-plan-dims`) | `drafting-opening-dims` | `drafting-opening-dims.md` |
| Door/window tags (elevation, section; plans → `drafting-plan-dims`) | `drafting-opening-tags` | `drafting-opening-tags.md` |
| Stair core **plan** | `drafting-stair-plan` | `drafting-stair-plan.md` |
| Stair **section along the flights** (parallel to the stair path) | `drafting-stair-section-parallel` | `drafting-stair-section-parallel.md` |
| Stair section **across** the flights (not parallel: flights seen end-on) | `drafting-stair-section-cross` | `drafting-stair-section-cross.md` |
| Floor plan dimension drawings (khai triển mặt bằng kích thước): door/window dims and tags on plans, tag room, floor spot elevations, wall tags, slope marks… | `drafting-plan-dims` | `drafting-plan-dims.md` |
| Floor finish plan (khai triển mặt bằng hoàn thiện sàn): tag cao độ sàn, ký hiệu độ dốc, mã hoàn thiện sàn… | `drafting-plan-floor-finish` | `drafting-plan-floor-finish.md` |
| Reflected ceiling plans (khai triển mặt bằng trần): tag room… | `drafting-ceiling-plan` | `drafting-ceiling-plan.md` |
| Room tags (fix outside / "?"), view titles | `drafting-tags-titles` | `drafting-annotation.md` |
| Viewports, crops, templates, links, drawing list + print set | `drafting-views-sheets` | `drafting-views-sheets.md` |
| Checking by image | `drafting-visual-check` | — |

- A task touching two topics → load the two rows, nothing else.
- `drafting-tools.md` and `drafting-api-pitfalls.md` are look-up lists: search them for the one command or symptom you need (`grep`). Do not read them whole.
- The `related` list in a standard's header is for maintainers. Do not open those files because of it.
- A command's parameters: read the header comment of that one `.cs` file only when unsure.
- Revit MCP tools are deferred (tool search). Load the commands on the chosen skills' **Tools** lines in **one** ToolSearch call (`select:` + the full names, e.g. `mcp__<server>__dims_text`), not one search per command.

## Hard rules (recite before acting)

1. Annotation, 2D and view settings only. Never edit walls, doors, rooms, 3D grid/level extents or links.
2. No sync unless asked this turn. Never touch the models the profile marks off-limits.
3. Work in **batches of up to 10 views of one kind** (same Revit ViewType + same topic), step by step for the whole batch. Elevation/section commands stay ONE view per call, with `timeoutSeconds`.
4. "dừng" / "tạm dừng" → stop immediately and report where you stopped.
5. No "Claude"/"AI" in anything written to the model.
6. Hide via View Template, not Hide in View.
7. Never "Replace with text" on a dim: the value stays live; extra text goes in Prefix / Suffix / Above / Below (`dims_text`).
8. Everything you create gets a **mark type** `<official type name><profile suffix>`; back to official only when the user approves.

## Start

1. Re-anchor: `hello_revit` or `get_active_view`. Never reuse ids from an earlier turn or a summary without checking.
2. Find the task source (comment PDF, TASKS.md or the user's message) and the latest log in `review/<date>_<code>/`.
3. **Form the batch**: up to 10 views of the same ViewType (floor plans with floor plans, sections with sections, elevations with elevations) and the same topic. More views → several batches, one after another. Show the batches in one short table.
4. Open the batch views, hide Properties and Project Browser, show the status window: follow `drafting-start`.
5. **Snapshot** the batch before any change: `mark_types {action:"snapshot", viewIds:[batch], statePath:<review>/batch-<n>-snapshot.json}`.

## During

- Run each step of the skill for the whole batch before the next step: survey all → preview all → apply all (with `logPath`) → check all. Commands taking `viewIds`: one call for a batch of plans; elevations/sections one view per call. **1V** commands: view after view, no report in between. A view that fails → note it, carry on, report it at the end.
- **Mark types** (`drafting-work-rules.md` §2c):
  - The **check** dim type = the official dim type + the profile suffix. Create it once with `mark_types {action:"ensure", suffix, types:[<official dim type>]}` and pass its name where a command asks for a dim type.
  - After applying: `mark_types {action:"mark", suffix, statePath, mode:"preview"}` → `apply` with `logPath`. Every element new in the batch views (tags, spots, room tags, filled regions, text…) gets its mark type. Run it again after fixes (already-marked elements are skipped) and once more before the report.
  - Detail lines and edits to existing elements cannot be marked: they stay in the `apply` logs.
- After the batch: export images **of the views** in one call (`export_sheet_images {sheetNumbers:[], viewIds:[batch]}`) and hand them all to one checker (`drafting-visual-check`). No computer-use screenshots unless the user asks.
- Give the user a one-line progress update when they have not heard from you for a while.
- Spend few tokens: prefer a topic's one-shot command (`*_annotate`, `*_band`) over many small calls; ask for `verbose`/`detail` output only when the short result does not answer; search long files (logs, catalogs) with `grep`, never read them whole.
- Images: hand the image reading to the `drafting-visual-checker` subagent (see `drafting-visual-check`); keep only its text findings.

## Done = ĐỦ – ĐÚNG – ĐẸP (`drafting-work-rules.md` §2b)

Before calling a batch finished, check all three for each of its views:
- **Đủ (complete)**: every item of the topic's standard is on the view; the topic audit shows no `missing` except items the user agreed to drop. Never skip an item silently ("the project does not use it" needs proof from the project's own finished views); otherwise ask or list it under Việc tồn with the reason.
- **Đúng (correct)**: numbers from the model, references on real geometry at the edge the standard names, values match, every new dim/tag is actually drawn (`view_elem_boxes` Box ≠ null, seen on the image).
- **Đẹp (neat)**: `annotation_overlaps` = 0, dim lines evenly spaced and aligned, short-segment texts moved apart, short leaders, same look as the finished views on the same sheet.

## End of turn

1. Last `mark_types {action:"mark", …, mode:"apply"}` of the batch; write a log entry to `log/YYYY-MM.md` (batch views, snapshot and log files).
2. Add new lessons:
   - general → the matching domain file;
   - project-only → the profile.
3. Close the status window. The panes stay hidden between turns; when the user ends the work session (kết thúc làm việc), follow `drafting-end`, which brings them back.
4. Report:
   - exact Revit names (sheet no. – name, view, View Template);
   - one table for the batch: view → what was done → counts, and the marked elements per mark type;
   - end with "Cần xem" and "Việc tồn" (elements still in mark types: `mark_types {action:"list", suffix}`).
