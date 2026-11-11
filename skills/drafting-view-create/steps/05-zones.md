# 05 · Dependent views per zone (view theo vùng)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` VC9, `drafting-sheet-layout.md` §7 · **Tools** `view_create`, `view_crop_info`
**Applies to** the register rows with a `Vùng` (zoned plans).

## Do

1. For each zoned parent plan (made in card 02): one **Duplicate as Dependent** per zone scope box. Name = parent name + ` - VÙNG <x>`.
2. The parent stays off the sheets. The level overview plan (Z4) is its own view at the small scale.
3. Check every dependent:
   - its scope box is the zone of its register row;
   - the zone scheme is the same on every level and every plan kind (Z1).
4. `view_create {items:[{kind:"dependent", sourceViewId:<parent>, scopeBox, name}]}`: `preview` → `apply` with `logPath`. Read back with `view_crop_info` (scope box per view).

## Done when

- Every zoned row has its dependent view with the zone's scope box (`view_crop_info`).
- The same zones on every level and plan kind.

## Skip only if

No row has a zone (`drafting-sheet-layout` card 03 found no zone needed).

## Never

- Draw a different crop per dependent instead of the shared scope box.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
