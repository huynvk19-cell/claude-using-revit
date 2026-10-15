# 03 · XA2 Levels (dim cao độ tầng)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA2 · **Tools** `level_dims_add`, `dims_edit`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

```
level_dims_add {viewId, side:<opposite of XA1>, typeName:<check type>, mode:"preview", logPath}  → mode:"apply"
```

- As LA2: chain through the levels **and the plan-cut markers** of the stair plans (Generic Annotation triangles numbered, dashed line across the core) → `1500 | 5000 | 4000 | 1500 | …`; overall outside.
- A chain with 0 mm segments (stray witness) → delete it (`dims_edit`, log), rebuild.

## Done when

- The chain on the side opposite XA1, every level and plan-cut marker, overall outermost, no 0 mm segment.

## Skip only if

The view already has a clean level chain.
