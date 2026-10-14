---
name: drafting-visual-check
description: "Verify Revit drawing changes visually: export sheets to PNG, crop regions, inspect for overlaps, missing or duplicate dims/tags, titles off-centre. Use after any drafting change, before reporting, or when the user asks kiểm tra bản vẽ, xuất ảnh, xem lại sheet."
---

# Visual check

**Tools** (load all in one ToolSearch call): `annotation_overlaps`, `export_sheet_images`, `highlight_elements`.

1. **Export:** a batch of views in one call: `export_sheet_images {sheetNumbers:[], viewIds:[batch], folder:<temp>\<topic>}`; whole sheets: `export_sheet_images {sheetNumbers:[...], folder:<temp>\<topic>, pixelWidth:8000}`.
2. **Hand the images to the `drafting-visual-checker` subagent** (Agent tool): give it all the PNG paths of the batch in one call, what changed and the topic's checklist (step 5). It reads and crops the images and replies with a short fault list, so the images never enter the main conversation. Read an image yourself only to confirm one fault it reports, cropped to that spot.
   - Without the subagent (not installed): read the full sheet once (it displays at about 2000 px), then crop as in step 3.
3. **Crop the areas of interest** (the subagent does this itself):
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
6. **Fix, re-export only the affected sheet or view, and check again** (subagent again).
7. **Report what you could not fix** under "Cần xem".
