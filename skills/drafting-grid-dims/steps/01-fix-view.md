# 01 · Fix (sửa dim trục, mỗi lần một view)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` G1–G4 · **Tools** `grid_dims_band`, `ui_view_focus`
**Applies to** every view of the job; plans may go many per call, elevations / sections one view per call.

## Do

```
grid_dims_band {mode:"preview", viewId, dimType, preferSides, [deleteExtra], [extendGrids], compact:true}
grid_dims_band {mode:"apply",   viewId, ..., logPath:"<review>/grid-band-<viewId>.json"}
```

1. Open the view for the user first: `ui_view_focus {action:"open", viewId}`.
2. Read `Done` from the preview: created, moved, deleted / hidden, grid ends extended, annotation crop growth. Apply with the same arguments.
3. `After`: every group `OK`.

Dependent views: the dims belong to the parent. A moved dim "also shown in <view>" changed that view too → audit and fix that sibling next. New dims showing up in a sibling are hidden there automatically. Dependents with different crops sharing one parent chain: keep the chain for one view; in the other pass `extraIds:[chainId]` (hidden there) so it gets its own chain.

## Done when

- `After` shows every group `OK`: one chain + one overall per group, one side, in the band outside the crop and inside the bubbles, none clipped.

## Skip only if

Nothing to fix (audit `OK`), or manual only (listed).

## Never

- `deleteExtra` / `extendGrids` without the user's yes in this turn.
- Several elevations or sections in one call.
