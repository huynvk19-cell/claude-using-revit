# 01 · SB1 Run tags (tag vế thang)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB1 · **Tools** `stair_plan_annotate`
**Applies to** every plan view of the job, one view per call.

## Do

```
stair_plan_annotate {viewId, mode:"preview", parts:["runTags"]}
stair_plan_annotate {viewId, mode:"apply", parts:["runTags"], logPath:"<review>/stair-tags-<viewId>.json"}
```

- One tag per untagged seen run. By default (`runTagPlace:"outside"`) the head sits just outside the side wall of the run's lane (`runTagOffsetMm`, default 5 paper mm), text along the run, leader into the **seen part** of the run. The SA2 chain (card 04) then goes outside these tags.
- **Run tag type**: projects often have one stair run tag type per base level (it adds that level to the run's relative elevations). Preview and read `Text`: `From EL … To EL …` must be the run's absolute elevations. Wrong → preview the next type (`runTagTypeName`); never guess from the type name. Record the stair → type pairs in the profile.
- Another tag type for one stair: run `parts:["runTags"]` with the other stairs in `excludeStairIds` after the rest is placed. Do **not** exclude a core stair to target one run otherwise: the seen / hidden test needs all of them.
- The command only adds what is missing: re-running it on a half-done view is safe. Undo: `mode:"undo", logPath`.

## Done when

- One tag per seen run (typically V1, V2, V3); V1 and V3 share a lane → two tags on the same side, each level with its own seen part.
- Every `Text` reads the run's real `From EL … To EL …` and `…T`.
- Heads outside the side wall, leader with arrow into the seen part, no dim over a tag.

## Skip only if

Never: every seen run gets its tag.

## Never

- Put a run tag under a dim.
- Leave the head box on a wall notch or a dim (leaders orthogonal: `_tags-common.md`).
