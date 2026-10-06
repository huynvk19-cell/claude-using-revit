---
name: drafting-visual-check
description: "Verify Revit drawing changes visually: export sheets to PNG, crop regions, inspect for overlaps, missing or duplicate dims/tags, titles off-centre. Use after any drafting change, before reporting, or when the user asks kiểm tra bản vẽ, xuất ảnh, xem lại sheet."
---

# Visual check

1. **Export:** `export_sheet_images {sheetNumbers:[...], folder:<temp>\<topic>, pixelWidth:8000}`.
2. **Read the full sheet once** (it displays at about 2000 px).
3. **Crop the areas of interest:**
   ```
   ~/.claude/drafting-domain/tools/crop.ps1 -In <png> -Out <png> -Cx 0.5 -Cy 0.5 -W 0.3 -H 0.15
   ```
   - Cx/Cy is the centre and W/H the size, all as fractions of the image.
   - The crop is scaled to 1600 px.
4. **Run `annotation_overlaps` per view** for a measured list. Colour the result red with `highlight_elements` when the user asks (logPath, `undo` removes it).
5. **Look for:**
   - text on text, or dims crossing tags;
   - chains outside the building or above the roof;
   - long bridging segments;
   - dims or tags on openings that are not visible;
   - duplicates of existing dims;
   - titles that are off-centre or overlapping;
   - grid bubbles that overlap.
6. **Fix, re-export only the affected sheet, and look again.**
7. **Report what you could not fix** under "Cần xem".
