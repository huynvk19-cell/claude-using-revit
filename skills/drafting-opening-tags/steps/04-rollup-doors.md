# 04 · Roll-up doors (cửa cuốn)

**Skill** `drafting-opening-tags` · **Standard** `drafting-opening-tags.md` T5 · **Tools** `elevation_opening_tags`
**Applies to** every elevation / section of the job, one view per call (`viewIds:[one id]`, `maxSeconds`).

## Do

- **Large door** (tag width ≤ 1/3 and tag height ≤ 1/4 of the door's visible size): the tag may sit **inside**, centred, about mid-height, no leader, clear of the door's own dims, tags and room tags. `elevation_opening_tags` does it itself (`rollupInside` on by default; `rollupFamilies` = the profile's roll-up family name).
- **Small door**: tag above, like a door; leader end ~2.5 mm inside the visible top, not on the coil box.
- Other door types are never tagged inside.

## Done when

- Every roll-up door tagged per its size.

## Skip only if

The view has no roll-up door.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
