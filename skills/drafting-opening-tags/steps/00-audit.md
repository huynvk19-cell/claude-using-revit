# 00 · Audit (khảo sát tag cửa)

**Skill** `drafting-opening-tags` · **Standard** `drafting-opening-tags.md` T1–T8 · **Tools** `door_window_tag_check`
**Applies to** every elevation / section of the job, one view per call (`viewIds:[one id]`, `maxSeconds`).

## Do

```
door_window_tag_check {mode:"audit", viewIds:[id], viewTypes:["Section","Elevation"], untaggedOnlyInTaggedViews:false}
```
Lists untagged openings; duplicate tags (two on one opening); orphaned tags and tags showing "?" or empty; tag heads crossed by the opening's own linework.

## Done when

- Every view audited; the list per view.

## Skip only if

The view shows no opening.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
