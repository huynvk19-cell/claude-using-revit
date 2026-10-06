---
name: drafting-opening-dims
description: "Dimension every visible window and door (incl. roll-up doors) on elevation and section views, ONE OPENING AT A TIME: vertical chain host level → sill → head, horizontal chain grid → edge → edge → grid, placed right next to the opening, referencing the opening itself; then tidy overlaps. Use for dim cửa sổ, dim cửa đi, dim cửa cuốn, dim chiều cao cửa, dim khoảng cách cửa, dim mặt đứng, dim mặt cắt, cửa thiếu dim, opening dimensions."
---

# Opening dims (elevations / sections)

Standard: `~/.claude/drafting-domain/drafting-opening-dims.md`. Rules Q1–Q5 decide every case. Read it first.

The user requires the work to go **opening by opening**. A row/batch tool that counts a dim as "existing" by position leaves openings without dims. So never trust a batch result: audit by reference.

## Steps — ONE view per call

1. **Audit** (read-only, once per view, can take 1–2 min):
   ```
   opening_dims_each {viewId, mode:"audit", cachePath:<review>/each-<view>-cache.json}
   ```
   The audit:
   - finds the openings that are really visible;
   - checks, by reference to the opening, whether each has a V chain (host level / sill / head) and an H chain (both edges);
   - lists only the openings with gaps.
2. **For each listed opening, one call:**
   ```
   opening_dims_each {viewId, mode:"apply", openingId, cachePath, logPath}
   ```
   It adds only what that opening is missing, on the nearest clear line beside it:
   - V: host level → bottom → top.
   - H: nearest grid → L → R → nearest grid, just above the head or below the sill, inside the storey.

   It also handles these cases:
   - **Own partial V pieces:** check-type pieces on the opening are replaced by one full chain.
   - **Stacked openings:** openings with identical edges, e.g. a louvre over a window, share one H chain ("ok (shared)").
   - **Leaf vs frame:** a black dim within 60 mm of the frame top counts, e.g. 2600 leaf vs 2650 frame.
3. **Re-audit until `MissingV` = `MissingH` = 0.**
   - Bad placement (`Notes` with many clashes, "outside crop", no grid on one side) → delete that dim with `dims_edit` and report it for manual placement.
4. **Tidy:**
   ```
   dims_declutter {viewId, mode:"apply", cachePath, logPath}
   ```
   - It moves check-type dims sideways in 1.2 mm steps, away from texts, tags, openings and cut slabs/beams.
   - Black dims move only by explicit `ids`, and only when they really overlap text.
5. **Verify:**
   - Run `annotation_overlaps` per view.
   - Export the sheet images and look (`drafting-visual-check`).
   - Long old chains that cannot be cleared (crossing many grids and slabs) → delete them and let step 2 rebuild per opening.
6. **Report per view:**
   - openings fixed;
   - what is left for manual work;
   - remaining overlaps (coloured red with `highlight_elements` when asked).

## Never

- Dimension a hidden opening.
- Draw a vertical dim across its own opening.
- Push a dim into another storey.
- Reference a link.
- Batch several views in one call.
