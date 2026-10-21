# 06 · C Tread numbers (đánh số bậc)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` C · **Tools** `stair_plan_annotate`, `number_systems_copy`, `export_sheet_images`, `stair_plan_audit`
**Applies to** every plan view of the job, all in one call (`viewIds`).

## Do

```
stair_plan_annotate {viewIds:[<all plans>], mode:"preview", parts:["numbers"]}  → apply with logPath
```

- Numbers on each seen run without them (`numberSide`, default `left`). V1 gets the mirrored side, so the V1 and V3 columns never overlap in their shared lane.
- **Settings**: copy them from a finished view of the same core with `number_systems_copy {sourceId, targetIds}` (Display Rule, Number Size, Justify… copied as stored values).
- **Numbering across stairs**: the start number is the Stairs parameter "Tread/Riser Start Number" (a model parameter: ask the user once for permission). Start = previous stair's start + its Actual Number of Risers, from the lowest stair of the core; list the whole chain (stairs id → base/top level → risers → start) in the profile.

## Done when

Check: `stair_plan_audit {viewIds, rules:["C"]}` (`TreadNumbers`, C issues) + the image.

- Every seen run numbered.
- Numbers continue over the whole stair from the lowest riser of the building (sample: 1…15, 16…30, 31…45), every second one shown, odd numbers; each run starts at the previous run's last number + 1 (checked on the image).
- V1 / V3 columns on opposite sides; no number on another number, tag or arrow.

## Skip only if

The profile says tread numbers are not shown.

## Never

- Setting a length parameter with `modify_element_parameter`: it parses the text as feet ("2" → 610 mm, the whole view turns black).
- Changing the start numbers without the user's permission (R1).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
