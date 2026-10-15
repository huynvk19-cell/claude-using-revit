# 01 · Place tags (đặt tag)

**Skill** `drafting-opening-tags` · **Standard** `drafting-opening-tags.md` T1–T4, T6, T8 · **Tools** `elevation_opening_tags`
**Applies to** every elevation / section of the job, one view per call (`viewIds:[one id]`, `maxSeconds`).

## Do

```
elevation_opening_tags {viewIds:[id], mode:"preview", addLeader:true, maxSeconds:90}
elevation_opening_tags {viewIds:[id], mode:"apply", addLeader:true, fixExisting:true, logPath}
```
- The preview finds the openings really visible (rays), plans a tag for each untagged one, reports wrong existing tags (on host, door tag not above, window tag beside, far, covering something).
- `fixExisting` moves only the wrong tags. Tags that still cover a dim or a hard element are coloured red.

## Done when

- Exactly one tag per visible opening: above, centred, touching, short leader; none over dims, tags or hard elements.

## Skip only if

No untagged or wrong tag in the audit.

## Never

- `repositionAll` on a view the user adjusted by hand (T8).
- A tag head on its own opening (except a large roll-up door, card 04); a window tag beside its window.
