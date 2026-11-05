# 02 · Compare with the model (đối chiếu model: thiếu / thừa)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §1, §9 · **Tools** `cur_task`, `query_elements_with_filter`, `sheet_browser_names`, `sheet_views_ids`
**Applies to** once per job (the whole set).

## Do

1. Read the model once:
   - every sheet and its views: `sheet_views_ids` once per prefix, `sheet_browser_names`;
   - the drawing list: `cur_task {task:"schedule_sheets", args:{schedule}}`;
   - views not on a sheet: `query_elements_with_filter {category:"Views", returnFields:["Sheet Number","View Scale","View Template"]}`. A view whose sheet number is empty or `---` is not on a sheet. Leave out view templates, 3D working views and the users' own views.
2. Match each target row (card 01) with a view and a sheet. Match on the view name, the level, the zone (scope box) and the ViewType.
3. Sort each row into one state:

   | State | Meaning | Next |
   |---|---|---|
   | ok | view on a sheet | card 05 / 07 check it |
   | not placed | view exists, no sheet | card 04 / 05 / 06 |
   | no view | drawing needed, no view in the model | ask the user: they make it, or it goes to Việc tồn |
   | extra | sheet / view in the model, not in the target | ask: keep (add to target) or leave out of the set; never delete |

4. Add the state column to the target list file. Report the counts per state, and list every `no view` and `extra` row.

## Done when

- Every target row has a state.
- Every `no view` row is answered by the user (they make it, or it goes to Việc tồn) or listed under Việc tồn.
- Every `extra` row is answered (keep / leave out).

## Skip only if

Never.

## Never

- Create model views yourself to fill a `no view` row (the user decides what is modelled and how).
- Delete an `extra` sheet or view.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
