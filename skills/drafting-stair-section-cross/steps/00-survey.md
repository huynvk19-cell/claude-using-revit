# 00 · Survey (khảo sát)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` §0, all rules · **Tools** `stair_xsection_info`, `view_dims_snapshot`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).

## Do

```
stair_xsection_info {viewId, outPath:"<review>/xsec-<viewId>.json", excludeStairIds?}
```

- `Error` "every flight runs across the view" → it is a longitudinal section: switch to `drafting-stair-section-parallel`.
- `Flights`: F1… going up, `Half` (left / right), `Facing` (`away` = risers seen → numbers; `toward` = soffit seen → no numbers), `TagText`, `RiseText`, run tags with their text, `FirstNumber` vs `FirstByElevation`. **Never count risers from the drawing.**
- `Slabs`: landings and floors with top, soffit, thickness, the x range the section cuts, `Cut`.
- `Dims`: XA1 per band (OK / missing / Replace with text / wrong prefix or below), XA2, XA3 per stair block, XB3 spots.
- `ClearHeights`: per half, the XA4 chain expected from the model (clear | thickness …) and what is missing.
- `Issues`: merged-band segments, 0 mm segments, duplicate dims, wrong tag type (From/To EL off), numbers on the wrong flights, Display Rule, numbering not continuous.
- Hand dims already on the view: `view_dims_snapshot` (`Refs`) before treating a view as a sample.

Show the user one table per view: band → flights (half, facing) → risers × height → tag text → numbers; then the issues by rule.

## Done when

- Every view read, its JSON written; bands, halves and facing known.
- Every stop-and-ask case asked.

## Skip only if

Never. Stop and ask instead when: no host stairs (link); `LC:` model mismatch; numbering not continuous (start numbers are a Stairs property: R1, the user decides).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
