# 00 · Template of each drawing kind (khoá template theo loại bản vẽ)

**Skill** `drafting-view-template` · **Standard** `drafting-view-template.md` §1, VT2–VT4, VT6 · **Tools** `view_templates_apply`
**Applies to** once per job (every kind of the register).

## Do

1. From the register, list the distinct triples `Loại` · `Loại view` · `Tỷ lệ` of the rows that have a `View id`. A dependent view counts through its parent (VT5).
2. Give each triple its key from table §1. A view kind marked `—` gets no template: note `—` for its rows.
3. Resolve each key to a template name in the profile, in the order of VT2 (`key@scale` → `key` → the fallback key → ask).
4. Check the names in the model in one call: `view_templates_apply {mode:"list", items:[{template:"<name>"}, …]}`.
   - `NotFound` → the template is missing (VT3).
   - `Type` must fit the view kind of the triple (VT4).
   - `Scale` other than 0 must equal the triple's scale (VT8).
5. Missing key, missing template, wrong type, wrong scale → ask the user once, all of them in one table. Write the answers into the profile.
6. Write the mapping table: `Loại` · view kind · scale · key used · template name. One template per row (VT6).

## Done when

- Every triple has one existing template that fits its view kind and scale, or is marked `—`.
- The mapping table is in the checklist notes and the profile holds every key used.

## Skip only if

Never.

## Never

- Take a template because its name looks right (VT2).
- Use the `plan` template for a `thematic` row.

## Score (0–10)

- −3 for each triple left without a template and without a question to the user.
- −2 for each kind split over two templates with no line in the profile (VT6).

**Accepted only above 7** (`drafting-work-rules.md` §2b2).
