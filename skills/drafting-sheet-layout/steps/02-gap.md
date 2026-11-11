# 02 · Compare with the model (đối chiếu model: thiếu / thừa)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §1, §9 · **Tools** `cur_task`, `query_elements_with_filter`, `sheet_browser_names`, `sheet_views_ids`
**Applies to** once per job (the whole set).

## Do

1. Read the model once:
   - every sheet and its views: `sheet_views_ids` once per prefix, `sheet_browser_names`;
   - the drawing list: `cur_task {task:"schedule_sheets", args:{schedule}}`;
   - views not on a sheet: `query_elements_with_filter {category:"Views", returnFields:["Sheet Number","View Scale","View Template"]}`. A view whose sheet number is empty or `---` is not on a sheet. Leave out view templates, 3D working views and the users' own views.
2. Match each register row (card 01) with a view and a sheet. Match on the view name, the level, the zone (scope box) and the ViewType.
3. Sort each row into one state:

   | State | Meaning | Next |
   |---|---|---|
   | ok | view on a sheet | fill `Sheet id`, `View id`, `Viewport id`; state `placed` (card 04 checks it) |
   | not placed | view exists, no sheet | fill `View id`; `drafting-sheet-create`, then `drafting-sheet-place` |
   | no view | drawing needed, no view in the model | `drafting-view-create` makes it |
   | extra | sheet / view in the model, not in the target | ask: keep (add to target) or leave out of the set; never delete |

4. Fill the `Có sẵn` column (and the ids above) in the register. Report the counts per state, and list every `no view` and `extra` row.

## Done when

- Every register row has a `Có sẵn` value.
- Every `extra` row is answered (keep / leave out).

## Skip only if

Never.

## Never

- Create views in this card: that is `drafting-view-create`, after the sheets exist.
- Delete an `extra` sheet or view.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
