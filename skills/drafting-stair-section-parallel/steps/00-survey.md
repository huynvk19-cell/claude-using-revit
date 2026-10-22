# 00 · Survey (khảo sát)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` §0, all rules · **Tools** `stair_section_info`, `view_dims_snapshot`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

```
stair_section_info {viewId, outPath:"<review>/stair-sec-<viewId>.json"}
```

- `Flights`: F1, F2… going up; `State` (cut / beyond), `Rises` (left / right), `TagText`, `RiseText` (`169.4mm x 16R = 2710`), `GoingText` (`280mm x 15T = 4200`), run tags, riser numbers. **Never count risers from the drawing.**
- `Landings`: elevation and side.
- `Dims`: each flight's rise (LA1) and going (LA3) dim, `OK` / `missing` / wrong text; LA5 one row per cut landing.
- `RunTagTypes`: the most-used run tag types in sections and elsewhere.
- `Issues`, `Notes`. A note that flights run toward the viewer → this is a cross-section: switch to `drafting-stair-section-cross`.
- Hand dims already on the view: `view_dims_snapshot` (keep them, copy their layout).

Show the user one table per view: flight → state → risers × height → treads × depth → tag text; then the issues by rule.

## Done when

- Every view read, its JSON written.
- Flights cut / beyond known; every stop-and-ask case asked.

## Skip only if

Never. Stop and ask instead when: no host stairs (link); `LC:` model mismatch; no run tag type used in sections.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
