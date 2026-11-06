# 00 · Structural fill (fill kết cấu bị cắt)

**Skill** `drafting-section-general` · **Standard** `drafting-section-general.md` SF1–SF8
**Tools** `structural_cut_fill`, `export_sheet_images`
**Applies to** every general building section view of the job, usually at 1:200, one view per call.

## Do

For each view:

1. `structural_cut_fill {mode:"preview", viewId, sources, concreteType, steelType, suffix, lineStyle, groundLevel, pileCapPatterns, logPath}`, values from the profile row "Fill kết cấu".
2. Read the result:
   - `missingSources` not empty: the structural model is not loaded or is hidden in the view; fix the source before going on.
   - `officialInView` > 0: the view already has regions of the official types; compare with the preview so nothing is doubled.
   - `splitRegions` > 0: Revit refused the loops of one type as one region; say so in the report.
   - `undergroundHidden`, `pileCapsSkipped`, `noMaterialFoundations`, `unknown`, `notMergedPairs`: keep for the report.
3. `mode:"apply"` with the same arguments.
4. Export the view (`export_sheet_images {sheetNumbers:[], viewIds:[viewId], pixelWidth:9000}`) and look at crops of the floors, the foundations and the roof. Zoomed exports of a section can come out blank; crop the full export instead.

The command gives regions their mark types itself. **Do not run `mark_types mark` apply on this view** (`drafting-api-pitfalls.md`, 2026-11-06).

## Done when

- Every Structural Framing, structural Floor, Structural Foundation and Wall with Structural on cut by the view and inside the crop is filled, except what SF2 and SF7 leave out (`fails` empty, `unknown` listed in the report).
- The view has one region per type: one concrete, one steel. Concrete and steel use their own types; profiles of one type that touch merge, with no line between them.
- Underground, only the slab under the ground floor or the bottom basement slab is filled, plus what touches it or stands within a 50 mm joint of it (`touchGapMm`); no pile cap or pile is filled.
- All new regions use the profile's line style and a mark type.
- On the image, regions sit on their elements and cover no tag, dim or text.

## Skip only if

The view cuts no structural element: `structural_cut_fill` returns 0 regions.

## Never

- Fill finish floors or site floors (road, pavement, soil): only models in `sources` are read, never add an architectural or site model to them.
- Fill pile caps or piles, even where they touch a slab.
- Merge concrete with steel, or ask the user whether to.
- Use Hide in View, or edit the linked elements.
- Re-run over regions the user reshaped by hand without asking: the command replaces this user's regions of the mark types on the view. Type changes (hatch, colour) are kept.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
