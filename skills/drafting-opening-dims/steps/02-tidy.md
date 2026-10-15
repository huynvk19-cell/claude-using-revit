# 02 · Tidy (dọn dim cửa)

**Skill** `drafting-opening-dims` · **Standard** `drafting-opening-dims.md` Q5 · **Tools** `dims_declutter`, `annotation_overlaps`
**Applies to** every elevation / section of the job, one view per call, **opening by opening** (the user's rule).

## Do

```
dims_declutter {viewId, mode:"apply", typeName:<profile check type>, cachePath, logPath}
```

- Moves check-type dims sideways in 1.2 mm steps, away from texts, tags, openings and cut slabs / beams.
- Black dims move only by explicit `ids`, only when they really overlap text. Dims continuing another dim on the same line (stacked openings) are never moved.
- Long old chains that cannot be cleared (crossing many grids and slabs) → delete them, let card 01 rebuild per opening.

## Done when

- `annotation_overlaps` shows no dim over text, tag or opening.

## Skip only if

Never.
