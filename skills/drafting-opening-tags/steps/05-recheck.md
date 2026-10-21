# 05 · Recheck (kiểm lại)

**Skill** `drafting-opening-tags` · **Standard** `drafting-opening-tags.md` ĐỦ – ĐÚNG – ĐẸP · **Tools** `door_window_tag_check`, `annotation_overlaps`, `export_sheet_images`
**Applies to** every elevation / section of the job, one view per call (`viewIds:[one id]`, `maxSeconds`).

## Do

1. Audit again until no untagged or duplicate opening.
2. `annotation_overlaps` (tags vs dims / tags); export the image (`drafting-visual-check`).
3. Report per view: tags added, moved, deleted; what is still red; manual work left. After the user approves: `mode:"clearhighlight"` with the same `logPath`.

Undo restores everything: `mode:"undo"` with the `logPath`.

## Done when

- No untagged, no duplicate; overlaps = 0 or listed.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
