# 01 · Templates per view kind (template theo loại view)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` VC3, VC4 · **Tools** `query_elements_with_filter`
**Applies to** once per job (every template key of the creation table).

## Do

1. List the template keys the creation table of card 00 uses.
2. For each key, take the template name from the profile.
3. Check it exists: it appears in the `View Template` field of the views read in card 00, or in `query_elements_with_filter {category:"Views"}` (templates are views). Not found → it counts as missing.
4. Check its scale against the register's `Tỷ lệ` when the template locks the scale (VC4).
5. Missing key, missing template or wrong scale → ask the user once, for all of them together. Write their answers into the profile.

## Done when

- Every key of the creation table names a template that exists in the model, with a scale that matches the register (or is not locked).

## Skip only if

The creation table is empty (every row was reused).

## Never

- Create, duplicate or edit a view template.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
