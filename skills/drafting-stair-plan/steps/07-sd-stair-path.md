# 07 · SD Stair path

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SD · **Tools** `stair_plan_annotate`, `export_sheet_images`, `stair_plan_audit`
**Applies to** every plan view of the job, all in one call (`viewIds`).

## Do

```
stair_plan_annotate {viewIds:[<all plans>], mode:"preview", parts:["path"]}  → apply with logPath
```

- One **Fixed Up Direction** path per stair, UP/DOWN text off. An existing path keeps its place; its type and text are fixed.
- A Fixed Up path's `ShowUpText` property throws; the instance parameter "Show Up Text" works and is ON by default: the command turns it off (also on existing paths).

## Done when

Check: `stair_plan_audit {viewIds, rules:["SD"]}` (`Paths`, SD issues) + the image.

- One path per seen stair, Fixed Up type, no UP / DOWN text.
- On the image: a V-shaped arrow at the top end of each run, pointing **up** (from the lower run to the higher).

## Skip only if

Never.

## Never

- Create a path type the project does not have: ask.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
