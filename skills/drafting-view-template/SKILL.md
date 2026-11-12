---
name: drafting-view-template
description: "Put every view of the sheet set under the view template of its drawing kind (gán view template theo loại bản vẽ), for any building type: register kind → profile key → template name, preview against what each view has now, apply with a log, verify. Never creates or edits a template. Use for áp dụng view template, gán template, template theo loại bản vẽ, view chưa có template, sai template, khởi tạo hồ sơ."
---

# View template per drawing kind (gán view template theo loại bản vẽ)

**Tools** (load all in one ToolSearch call): `view_templates_apply`, `sheet_views_ids`.

Every card uses `view_templates_apply {items:[{template, viewIds, scale}], mode}` (contract: the standard §4): `list` reads the model's templates, `preview` shows what would change, `apply` needs `logPath`.

Standard: `~/.claude/drafting-domain/drafting-view-template.md` (Vietnamese): table §1 kind → template key, rules VT1–VT10. It is over 120 lines: read §1 once per job, then only the rule a card names.

**Input**: the sheet register (`drafting-sheet-layout.md` §11), every row that has a `View id` (created, reused or already in the model).
**Output**: column `Template` of the register = the real template name of each view.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the view template name for each key of table §1 (and `key@scale` where the project has a template per scale);
- the building type (only for the notes of the standard §2).

What a template shows or hides is not this skill: `drafting-views-sheets` card 04.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the register before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-keys.md` | Template of each drawing kind, from the profile (khoá template theo loại bản vẽ) |
| 01 | `steps/01-apply.md` | Preview, ask, apply (gán template) |
| 02 | `steps/02-verify.md` | Verify and update the register (kiểm, cập nhật sổ) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Create, duplicate, rename or edit a view template (VT3).
- Change a view that is under another template without the user's yes for that view (VT7).
- Guess a template from a similar name in the model (VT2).
- Change the register's scale, or a view's scale, to fit a template (VT8).
