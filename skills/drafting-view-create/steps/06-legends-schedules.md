# 06 · Legends, drafting views, schedules (legend, bảng thống kê)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1 · **Tools** `view_create`, `cur_task`, `query_elements_with_filter`
**Applies to** the creation table rows of kind legend, drafting, schedule (ghi chú ký hiệu, cấu tạo, chi tiết cửa, key plan, thống kê cửa).

## Do

1. **Legends**: ghi chú ký hiệu, cấu tạo sàn / trần / tường (Legend Components of the wall / floor / ceiling types), chi tiết cửa (Legend Components of each door / window type), key plan.
   - Revit cannot create a legend from scratch through the API: copy an existing legend of the project (`legendCopy`), or the user makes the first one.
2. **Drafting views**: ghi chú chữ, standard details not from the model.
3. **Schedules**: door and window schedules as a copy of the project's schedule template (`scheduleCopy`). Check them with `cur_task {task:"schedule_list"}`.
4. Names by VC2, template `legend` / `detail`, scale of the register.

5. `view_create`, `preview` → `apply` with `logPath`: `kind:"legendCopy"` / `"scheduleCopy"` with `sourceViewId`, `kind:"drafting"`.
6. Write the ids into the register.

## Done when

- Every legend / drafting / schedule row has its view (id in the register).
- The door / window schedules list every door / window type of the model (`cur_task schedule_table`, row count > 0).

## Skip only if

The table has no legend, drafting or schedule row.

## Never

- Type door / window data by hand into a schedule that should read the model.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
