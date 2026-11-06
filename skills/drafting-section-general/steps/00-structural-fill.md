# 00 · Structural fill (fill kết cấu bị cắt)

**Skill** `drafting-section-general` · **Standard** `drafting-section-general.md` SF1–SF6
**Tools** `structural_cut_fill`, `export_sheet_images`
**Applies to** every general building section view of the job, usually at 1:200, one view per call.

## Do

For each view:

1. `structural_cut_fill {mode:"preview", viewId, sources, concreteType, steelType, suffix, lineStyle, logPath}`, values from the profile row "Fill kết cấu".
2. Read the result:
   - `missingSources` not empty: the structural model is not loaded or is hidden in the view; fix the source before going on.
   - `officialInView` > 0: the view already has regions of the official types; compare with the preview so nothing is doubled.
   - `noMaterialFoundations`, `unknown`, `notMergedPairs`: keep for the report.
3. `mode:"apply"` with the same arguments.
4. Export the view (`export_sheet_images {sheetNumbers:[], viewIds:[viewId], pixelWidth:9000}`) and look at crops of the floors, the foundations and the roof. Zoomed exports of a section can come out blank; crop the full export instead.

The command gives regions their mark types itself. **Do not run `mark_types mark` apply on this view** (`drafting-api-pitfalls.md`, 2026-11-06).

## Done when

- Every Structural Framing, structural Floor and Structural Foundation cut by the view and inside the crop has a region (`fails` empty, `unknown` listed in the report).
- Concrete and steel use their own types; profiles of one type that touch are one region, with no line between them.
- All new regions use the profile's line style and a mark type.
- On the image, regions sit on their elements and cover no tag, dim or text.

## Skip only if

The view cuts no structural element: `structural_cut_fill` returns 0 regions.

## Never

- Fill finish floors or site floors (road, pavement, soil): only models in `sources` are read, never add an architectural or site model to them.
- Merge concrete with steel, or ask the user whether to.
- Use Hide in View, or edit the linked elements.
- Re-run over regions the user reshaped by hand without asking: the command replaces this user's regions of the mark types on the view. Type changes (hatch, colour) are kept.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
