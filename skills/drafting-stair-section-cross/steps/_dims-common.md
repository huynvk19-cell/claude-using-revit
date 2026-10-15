# Dims: shared notes (cards 02–07)

Read once per job, before the first dim card. No annotate command for cross-sections yet.

- Dim type: the **check** type of the profile.
- References as the project's hand dims (`view_dims_snapshot` → `Refs`): landing / floor tops and soffits on the **Stairs** element and Floors (run / landing 3D faces are hidden: dims on them are not drawn), walls, **Handrails**, never a link.
- Same stair already dimmed in another cross-section → `dims_copy_refs` (preview → apply). Otherwise ask the user for one hand dim per chain kind and rebuild from its references.
- After apply: `view_elem_boxes` → Box ≠ null for every new dim.
- Formula text (XA1): `dims_text {viewId, mode:"apply", logPath, items:[{dimId, valueMm, prefix:"169.4mm x 16R = ", below:"(EQUAL RISERS)"}]}`. A Replace-with-text value is cleared, never kept (R9). One spelling for the project; compare with `RiseText`.
- Each dimension once; never one chain on both sides.
