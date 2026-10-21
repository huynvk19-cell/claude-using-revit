# 03 · Align rows (căn tag theo hàng)

**Skill** `drafting-opening-tags` · **Standard** `drafting-opening-tags.md` T7 · **Tools** `tag_align`
**Applies to** every elevation / section of the job, one view per call (`viewIds:[one id]`, `maxSeconds`).

## Do

```
tag_align {viewId, mode:"apply", refTagText, tagTexts:[...], logPath}
```
Brings the tags of one row to the same height above their openings, with leaders.

## Done when

- Tags of one row of openings at one height.

## Skip only if

No row of two or more openings with tags.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
