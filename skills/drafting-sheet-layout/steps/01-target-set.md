# 01 · Target drawing set (bộ hồ sơ cần có)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §2, §3, §4 + the sheet-set file of card 00 · **Tools** `query_elements_with_filter`
**Applies to** once per job (the whole set).

## Do

1. Read the facts the list depends on, read-only:
   - levels: `query_elements_with_filter {category:"Levels", returnFields:["Elevation"]}`;
   - basement, mezzanine, roof, typical floors;
   - house types / blocks / zones from the profile.
2. Build the **target list**:
   - start from the core set (§4) + the sheet-set file's table;
   - keep the rows whose GĐ is not later than the job's stage, and whose condition holds ("nếu có tầng hầm"…);
   - expand "mỗi tầng" to one row per level, typical floors as one row with their level range;
   - expand per zone (if card 03 already split) and per house type / block.
3. One row = one drawing:

   | Group | Drawing | Level / zone / type | Scale | Stage | Source row |
   |---|---|---|---|---|---|

   "Source row" names the standard row the drawing comes from, so the user can see why it is needed.
4. Write it to `layout-target-<date>.md` (round folder or `log/`). Show the user the counts per group and the drawings that come from a condition. Then ask once: add / remove?

## Done when

- The target list file exists.
- Every mandatory row of §4 and of the sheet-set file is either expanded or written as "n/a + reason".
- The user confirmed the list, or added / removed rows (their changes noted with the date).

## Skip only if

A target list of this job, already confirmed by the user, exists and the type / stage / levels did not change since.

## Never

- Add drawings from another building type's file.
- Drop a mandatory row silently: n/a needs a reason (e.g. "không có tầng hầm").

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
