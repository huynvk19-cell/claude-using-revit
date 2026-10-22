# 03 · LA2 Levels (dim cao độ tầng)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA2 · **Tools** `level_dims_add`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

```
level_dims_add {viewId, side:<opposite of LA1>, typeName:<check type>, mode:"preview", logPath}  → mode:"apply"
```

- Chain through the levels **and the plan-cut markers** of the stair plans (Generic Annotation triangles numbered 1…7, dashed line across the core, at level + 1500 …) → `1500 | 5000 | 4000 | …`; overall outside.

## Done when

- The chain on the side opposite LA1, every level and plan-cut marker seen, overall outermost.

## Skip only if

The view already has the level chain.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
