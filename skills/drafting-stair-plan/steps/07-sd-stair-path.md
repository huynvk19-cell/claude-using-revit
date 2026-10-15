# 07 · SD Stair path

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SD · **Tools** `stair_plan_annotate`, `export_sheet_images`
**Applies to** every plan view of the job, one view per call.

## Do

```
stair_plan_annotate {viewId, mode:"preview", parts:["path"]}  → apply with logPath
```

- One **Fixed Up Direction** path per stair, UP/DOWN text off. An existing path keeps its place; its type and text are fixed.
- A Fixed Up path's `ShowUpText` property throws; the instance parameter "Show Up Text" works and is ON by default: the command turns it off (also on existing paths).

## Done when

- One path per seen stair, Fixed Up type, no UP / DOWN text.
- On the image: a V-shaped arrow at the top end of each run, pointing **up** (from the lower run to the higher).

## Skip only if

Never.

## Never

- Create a path type the project does not have: ask.
