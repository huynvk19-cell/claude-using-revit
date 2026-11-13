# 01 · Target drawing set (bộ hồ sơ cần có)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §2, §3, §4, §4b (CT1–CT3), §8 (TG1–TG6) + the sheet-set file of card 00 + `drafting-coverage.md` §1, §2 · **Tools** `query_elements_with_filter`
**Applies to** once per job (the whole set).

## Do

1. Read the facts the list depends on, read-only:
   - levels: `query_elements_with_filter {category:"Levels", returnFields:["Elevation"]}`;
   - basement, mezzanine, roof, typical floors;
   - toilets, stairs and lifts: one of each type, the levels each serves (a lift is known by its family, shaft opening or room name of the profile, CT3);
   - house types / blocks / zones from the profile.
2. Build the **sheet register** (`drafting-sheet-layout.md` §11), one row per drawing:
   - start from the core set (§4) + the sheet-set file's table; a sheet-set file with a **Danh mục điển hình** replaces the core set, and its row order is the sheet order;
   - keep the rows whose GĐ is not later than the job's stage, and whose condition holds ("nếu có tầng hầm"…);
   - expand "mỗi tầng" to one row per level, typical floors as one row with their level range;
   - expand per zone (if card 03 already split) and per house type / block.
3. Fill the columns of this card: `ID`, `Nhóm`, `Bản vẽ`, `Tầng / vùng / mẫu`, `Tỷ lệ`, `GĐ`, `Nguồn`, `Loại`, `Mức`. Every row starts in state `planned`.
   - `Loại` = the kind key of the standard row; `Mức` = that kind's level in `drafting-coverage.md` §2. A row with several kinds gets one sub-row per kind.
   - `Nguồn` names the standard row the drawing comes from, so the user can see why it is needed.
   - A drawing made of several views gets sub-rows `ID.a`, `ID.b`, with every mandatory part of §4b: toilet = dimension plan + floor finish plan + wall elevations (CT1); stair = plans + sections + enlarged section details + nosing detail (CT2); lift, when the model has one = shaft plans + shaft section (CT3).
   - `Bản vẽ` holds the full name (TG1–TG4), with the component code of a repeated component (`ST1`, `WC1`…, TG6: only for a kind with two or more detail drawings, one code per drawing, numbered lowest level first, then by grid): the sheet and view names are copied from it.
4. Write it to `sheet-register-<date>.md` (round folder or `log/`). Show the user the counts per group, the drawings that come from a condition, and the counts per level in the words of `drafting-coverage.md` §1: **trọn** / **một phần** (with what is missing) / **làm tay**. Then ask once: add / remove?

## Done when

- The register file exists, every row `planned`, with its `Loại` and `Mức`.
- Every mandatory row of §4 and of the sheet-set file is either expanded or written as "n/a + reason" (no lift → "không có thang máy").
- Every toilet, stair and lift row has its sub-rows of CT1–CT3; no drawing name is abbreviated.
- The user confirmed the list, or added / removed rows (their changes noted with the date).

## Skip only if

A register of this job, already confirmed by the user, exists and the type / stage / levels did not change since.

## Never

- Add drawings from another building type's file.
- Drop a mandatory row silently: n/a needs a reason (e.g. "không có tầng hầm").
- Promise more than the level: a level 1 row is the user's to draw; say so before the work starts.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
