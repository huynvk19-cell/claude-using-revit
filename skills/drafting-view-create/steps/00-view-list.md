# 00 · View list from the register, reuse existing (danh sách view cần tạo)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1, VC1 · **Tools** `query_elements_with_filter`, `sheet_browser_names`
**Applies to** every register row in state `sheet`.

## Do

1. For each row, read its Revit view kind from table §1. A drawing made of several views (stair = callout per level + sections; WC = callout + interior elevations) gets its sub-rows `ID.a`, `ID.b`… now, one per view.
2. Read the existing views once:
   `query_elements_with_filter {category:"Views", returnFields:["View Name","Sheet Number","View Template","View Scale","Associated Level"]}`
3. Sort each row:
   - **reuse**: a view of the same kind, level and zone exists and is on no sheet → write its id, name, template; state `view`;
   - **create**: no such view;
   - **conflict**: the view exists but is already on another sheet (LO11) → ask: duplicate it, or move the row.
4. Write `Loại view` for every row, and the creation table for the rows **create**:

   | ID | Loại view | Tạo từ | Level | Template (khoá) | Tỷ lệ | Tên view |
   |---|---|---|---|---|---|---|

## Done when

- Every row `sheet` has a `Loại view` and is reuse (with id), create (in the table) or conflict (answered by the user).

## Skip only if

Never.

## Never

- Create anything in this card (it only reads).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
