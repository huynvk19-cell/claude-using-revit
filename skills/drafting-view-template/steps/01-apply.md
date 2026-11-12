# 01 · Preview, ask, apply (gán template)

**Skill** `drafting-view-template` · **Standard** `drafting-view-template.md` VT1, VT5, VT7–VT9 · **Tools** `view_templates_apply`
**Applies to** every view of the register that is not marked `—` in card 00.

## Do

1. Build the items from the mapping table of card 00: one item per template, `{template, viewIds, scale}`. Split the views of one template over several items when their register scales differ.
2. Preview all of them in one call: `view_templates_apply {mode:"preview", items}`.
3. Read `Problems`:
   - `other template` → table `view · has · wants`, ask the user once which views may change (VT7);
   - `scale 1:x, register 1:n` → VT8: back to card 00 for that key;
   - `not valid for this view` → VT4: the row has the wrong key, back to card 00;
   - anything else → the row gets `!` with the message.
4. Apply: `view_templates_apply {mode:"apply", items, logPath:"view-templates-<date>.json"}`.
5. Views the user allowed to change: a second call with only those views and `replace:true`, with its own `logPath`.
6. A view that was already placed on a sheet and got a new template: note its sheet for a position recheck (VT9).

## Done when

- `Set` + `Replaced` + `Ok` = every view of the items, apart from the rows marked `!` or refused by the user.
- The log files are named in the checklist notes.

## Skip only if

The preview returns every view as `Ok` and no `Problems`.

## Never

- Pass `replace:true` for a view the user did not allow.
- Apply without the preview of this turn.

## Score (0–10)

- 0 when a view under another template was changed without the user's yes (VT7).
- −2 for each view left without a template and without a reason.

**Accepted only above 7** (`drafting-work-rules.md` §2b2).
