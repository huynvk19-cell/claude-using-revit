---
name: drafting-tools
description: "Danh mục lệnh động (dynamic commands) Revit MCP dùng cho triển khai bản vẽ, theo chủ đề, kèm tham số chính. Drafting tool catalog."
metadata:
  updated: "2026-10-06"
  related: ["drafting-work-rules", "drafting-dimensions", "drafting-annotation", "drafting-views-sheets"]
---

# Danh mục lệnh (Tool catalog)

**Yêu cầu**
- [Revit MCP](https://github.com/shuotao/REVIT_MCP_study) có hỗ trợ dynamic commands (`dynamic-commands/<tên>.cs`).
- Lệnh nào chưa có thì viết thêm. Lệnh viết bằng C# và được biên dịch ngay trong Revit, không cần khởi động lại.

**Mã nguồn**: thư mục `revit-commands/` của repo (cài bằng `install.ps1 -CommandsDir`). Tool không có giá trị mặc định của dự án nào: tên dim type, family cửa cuốn… truyền vào lúc gọi, lấy từ `drafting-profile.md`.

**Cách gọi**
- Gọi trực tiếp tool `<tên>`, hoặc qua `run_dynamic_command {name, args}`.
- Khi không chắc tham số: đọc header của file.
- Ký hiệu: RO = read-only; **1V** = mỗi lần gọi chỉ một view.

## Phiên làm việc

| Lệnh | Việc |
|---|---|
| `hello_revit` | Phiên bản Revit, document đang mở, active view. |
| `work_status` | Cửa sổ "Đang xử lý": show / update / close. |
| `export_sheet_images` | Xuất PNG sheet `{sheetNumbers, folder, pixelWidth}`. |
| `view_region_image` | Xuất PNG một vùng của view. |

## Tra cứu (RO)

| Lệnh | Việc |
|---|---|
| `sheet_browser_names` | Tên sheet/view theo Project Browser. |
| `sheet_views_ids` | View id, viewport id, viewport type, template theo tiền tố số sheet. |
| `view_crop_info` | Crop, annotation crop (offset mm giấy), scope box. |
| `list_view_dims` | Dim trong một view: theo type, theo phần tử, vị trí, giá trị. |
| `grid_dims_audit` / `grid_dim_rows` | Dim trục còn thiếu (logic cũ, theo hai phía) / các hàng dim trục hiện có. Chuẩn G1–G4: dùng `grid_dims_band audit`. |
| `hidden_dims_scan` | Dim không hiện (nằm ngoài crop). |
| `viewport_title_audit` | Kiểm tra title. |
| `template_link_visibility` | Link nào đang hiện trong từng template. |
| `rollup_dims_check` | Cửa cuốn và các dim đang trỏ vào nó (1V). |
| `stair_section_info` | **Mặt cắt dọc thang** (1V, section, `drafting-stair-section-parallel.md`): các vế F1… theo chiều đi lên (cắt / phía sau, lên trái / phải), From EL – To EL, R × chiều cao cổ bậc, T × độ sâu, chữ công thức; dim LA1/LA3 đã có (OK / thiếu / sai chữ), tag vế, số bậc; type tag vế dùng nhiều nhất trong mặt cắt. |
| `stair_plan_audit` | **Lõi thang bộ** (1V, mặt bằng, `drafting-stair-plan.md`): vế V1/V2/V3 theo mặt cắt, số bậc/độ sâu từ model + công thức, bề rộng thông thuỷ (tay vịn/tường), tường bao 4 phía, chiếu nghỉ; dim cần có (`Expected`: OK / thiếu / thiếu CLEAR / sai công thức), tag, cao độ, stair path, số bậc; type dùng nhiều nhất trong dự án. |
| `elements_beyond` | Nét chạy ra ngoài trục cuối. |
| `annotation_overlaps` | Annotation chồng lắp trong 1 view: chữ dim, tag, text note, cao độ điểm, đường dim cắt qua chữ; trả về id để `highlight_elements` tô đỏ (room tag mặc định bỏ qua). |
| `highlight_elements` | Tô màu (Override Graphics in View) cho id trong view, có log để `undo`. |

## Dim

| Lệnh | Việc / tham số chính |
|---|---|
| `grid_dims_band` | **Dim trục theo G1–G4** (`drafting-grid-dims.md`): `audit` (nhiều view, RO, bỏ view ẩn Dimensions): mỗi nhóm trục song song có đúng 1 chain + 1 overall trong dải giữa crop và bubble, annotation crop tới đầu trục có bubble; `preview` / `apply` (1 view): dời dim vào dải, tạo dim thiếu, nhận lại dim dùng chung của view cha, nới annotation crop; `deleteExtra` / `extendGrids` chỉ khi user duyệt; `forceSides`, `extraIds`, `compact`; `undo`. |
| `grid_dims_add` | Thêm chain + overall cho trục (một view, logic cũ: hai phía, sát bubble phía trong crop). |
| `grid_dims_layout` | Bố trí lại dim trục ở view cha: create / deleteSide / hideInViews / compact / undo. |
| `level_dims_add` | Chain + overall cho level (1V). |
| `elevation_opening_dims` | Dim cửa (1V): `strictVisibility:true`, `verticalMode:"all"`, `hostLevel:true`, `verticalOnly` / `horizontalOnly`, `nominalFamilies:["ROLL UP"]`, `moveExisting:false`, `gridNearDist:12000`, `logPath`. |
| `opening_dims_each` | **Dim từng cửa** (1V): `audit` liệt kê cửa thiếu dim đứng/ngang theo tham chiếu (nhóm cửa sát nhau, cửa chồng); `apply` + `openingId` bổ sung đúng phần thiếu, bổ sung điểm nối vào chuỗi có sẵn. |
| `openings_by_mark` | Tra cửa theo Mark trong một view và các dim đang trỏ vào nó. |
| `dims_declutter` | Dời dim (theo type) sang ngang từng bước 1.2 mm để hết chồng chữ/tag/cửa/sàn cắt. |
| `opening_vdims` | Dim đứng cho từng cửa chỉ định. |
| `rollup_dims_add` | Cửa cuốn theo TOP/LEFT/RIGHT; `fixChains` dựng lại chuỗi. |
| `dedupe_new_vdims` | Xoá dim đứng mới trùng giá trị với dim có sẵn. |
| `dims_split` | Tách chuỗi tại đoạn dài hơn `maxMm`. |
| `dims_edit` | Xoá / gộp (merge) / đổi type (retype) dim, có log. |
| `dims_text` | Prefix / Suffix / Above / Below của từng đoạn dim (giữ giá trị đo, không Replace with text): vd. ` CLEAR`, `280mm x 14T = `. Chọn đoạn theo `segmentIndex` hoặc `valueMm`; preview / apply / undo. |
| `dim_restore` | Dựng lại dim từ stable reference đã log. |
| `dim_type_check_copy` / `swap_dim_type` | Tạo type kiểm tra / đổi type hàng loạt. |

## Tag và title

| Lệnh | Việc |
|---|---|
| `stair_plan_annotate` | Lõi thang bộ (1V): stair path Fixed Up Direction, tắt chữ UP/DOWN (SD); số bậc từng vế, V1 phía đối diện V3 (C, `numberSide`); tag vế thang ngoài tường bên, leader vào phần nhìn thấy (SB1, `runTagPlace`). preview / apply / undo. |
| `elevation_opening_tags` | Tag cửa trên mặt đứng/cắt (1V), `addLeader`, `fixExisting`; cửa cuốn lớn: tag nằm trong cửa (`rollupInside`, `rollupFamilies`). |
| `tag_align` | Căn đầu tag theo tag mẫu. |
| `room_tags_outside` / `room_tags_broken` | Room tag nằm ngoài phòng / room tag "?". |
| `viewport_titles_place` | Bật và căn title (`ignoreCrop`). |
| `ui_view_focus` | Mở view + zoom theo crop để user theo dõi (`open`); ẩn/hiện Properties, Project Browser và báo trạng thái trước/sau (`panes` hide/show, `which` properties/browser). Dùng trong `drafting-start` / `drafting-end`. |

## View và trục

| Lệnh | Việc |
|---|---|
| `set_viewport_type` | Đổi viewport type (preview / apply / undo). |
| `crop_side_to_grids` | Kéo một cạnh crop tới sát trục ngoài cùng. |
| `align_grid_ends` | Căn đầu trục 2D (`compact`). |
| `grid_bubble_elbow` | Elbow cho bubble (hay lỗi → dùng so le đầu trục). |
| `view_image_trial` | Thử ẩn/bỏ template rồi xuất ảnh, luôn rollback. |
