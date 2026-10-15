# 13 · LB6 Doors, windows (tag cửa)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB6 · **Tools** `elevation_opening_tags`, `annot_place`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

Rules T1–T8 (`drafting-opening-tags.md`); **every door tag in a stair section has a leader** (user, 2026-10-08).

```
elevation_opening_tags {viewIds:[id], mode:"preview", addLeader:true}  → apply
```

- Door seen face-on → head ~190 mm above the door top, vertical leader ~130 mm into the door.
- The tool sees only doors facing the view: list the doors of the walls the section **cuts** (the core's rooms / levels) and tag each one it missed with `annot_place` — head **outside** the wall, **horizontal** leader into the door at about half its height.

## Done when

- Exactly one tag per door seen **and** per door in a cut wall, all with leaders.

## Skip only if

The section shows no door or window, and cuts no wall with a door.
