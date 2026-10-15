# 00 · Survey (khảo sát)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` §0, all rules · **Tools** `stair_plan_audit`, `view_range_info`
**Applies to** every plan view of the job, one view per call.

## Do

```
stair_plan_audit {viewId, excludeStairIds:[<stairs outside the core>], outPath:"<review>/stair-<viewId>.json"}
```

- **A neighbouring stair outside the core, partly in the crop**, breaks the audit (an extra lane, a side wall not found, every expectation shifted). Find it in `Runs` / `Lanes` (a lane beyond a core wall, a run cut by the crop) and re-run with `excludeStairIds`. Pass the same list to every later command.
- **V1 often belongs to the stair of the floor below and can sit under the View Depth** (its top = the floor = the view depth). Revit still draws every component of a stair the view collects; the audit counts a run / landing as seen when no higher run / landing covers it, and a below run under a cut run is V1.
- **Keep what the user already drew** (dims, tags, spots, numbers): later steps add only what is missing, and copy that layout to the views still to do.

Read, in this order:
1. `Runs`: label **V1 / V2 / V3** (beyond cut / full / cut), treads, tread depth, `Text` (e.g. `280mm x 15T = 4200`), `ClearWidth` and what bounds it (`ClearFrom` / `ClearTo`). These numbers come from the model: **never count treads from the drawing**.
   - **Treads (user rule)**: the top tread that sits at the landing / floor level is not counted: `Treads` = risers − 1 when the model has as many treads as risers (`TopTreadAtLanding: true`, e.g. 16R → 15T). `RunLengthMm` = first riser → last riser; the flush top tread belongs to the landing segment.
   - **Clear width bounds (user rule)**: railing edge, else the step / landing edge, else the finish wall face. A landing with a guard rail (shaft, opening) is measured to that rail, never across the opening.
2. `Lanes`: which run carries the lane's length dim, on which side, `V1V3Same`.
3. `Walls`: finish face and outer face per side; `ClearAcross`, `ClearAlong`; `Landings` (depth to wall, clear).
4. `Expected`: every dim the rules need, with `Status` (`OK` / `missing` / `no CLEAR suffix` / `CLEAR is only for the clear width of a run` / `prefix should be …`) and the matching `DimId`.
5. `Tags`, `Spots`, `Paths`, `TreadNumbers`, `ProjectTypes`, `Notes`, `Issues`.

Show the user one table per view: run → state → treads × depth → length → clear width; then the issues grouped by rule (SA, SB, C, SD).

## Done when

- Every view audited, its JSON written (`outPath`).
- The run / landing count matches what the view shows (a typical floor: **3 runs, 2 landings**). Fewer in the audit than on the view → say so, check `view_range_info`; never annotate a partial set.
- Every stop-and-ask case below asked and answered.

## Skip only if

Never. Stop and ask instead when:
- no host stairs, or the stairs are in a link;
- a side has no wall (open side, link walls) and a rule needs it;
- V1 and V3 differ (`V1V3Same:false`);
- treads × depth ≠ footprint length (`C:` issue): report it, never edit the model;
- a category has no tag type used in the project.
