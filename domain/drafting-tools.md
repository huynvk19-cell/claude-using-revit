---
name: drafting-tools
description: "Danh mục lệnh động (dynamic commands) Revit MCP dùng cho triển khai bản vẽ, theo chủ đề, kèm tham số chính. Drafting tool catalog."
metadata:
  updated: "2026-10-05"
  related: ["drafting-work-rules", "drafting-dimensions", "drafting-annotation", "drafting-views-sheets"]
---

# Danh mục lệnh (Tool catalog)

**Yêu cầu**
- [Revit MCP](https://github.com/shuotao/REVIT_MCP_study) có hỗ trợ dynamic commands (`dynamic-commands/<tên>.cs`).
- Lệnh nào chưa có thì viết thêm. Lệnh viết bằng C# và được biên dịch ngay trong Revit, không cần khởi động lại.

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
| `view_crop_info` | Crop, annotation crop, scope box. |
| `list_view_dims` | Dim trong một view: theo type, theo phần tử, vị trí, giá trị. |
| `grid_dims_audit` / `grid_dim_rows` | Dim trục còn thiếu / các hàng dim trục hiện có. |
| `hidden_dims_scan` | Dim không hiện (nằm ngoài crop). |
| `viewport_title_audit` | Kiểm tra title. |
| `template_link_visibility` | Link nào đang hiện trong từng template. |
| `rollup_dims_check` | Cửa cuốn và các dim đang trỏ vào nó (1V). |
| `elements_beyond` | Nét chạy ra ngoài trục cuối. |
| `annotation_overlaps` | Annotation chồng lắp trong 1 view: chữ dim, tag, text note, cao độ điểm, đường dim cắt qua chữ; trả về id để `highlight_elements` tô đỏ (room tag mặc định bỏ qua). |
| `highlight_elements` | Tô màu (Override Graphics in View) cho id trong view, có log để `undo`. |

## Dim

| Lệnh | Việc / tham số chính |
|---|---|
| `grid_dims_add` | Thêm chain + overall cho trục (một view). |
| `grid_dims_layout` | Bố trí lại dim trục ở view cha: create / deleteSide / hideInViews / compact / undo. |
| `level_dims_add` | Chain + overall cho level (1V). |
| `elevation_opening_dims` | Dim cửa (1V): `strictVisibility:true`, `verticalMode:"all"`, `hostLevel:true`, `verticalOnly` / `horizontalOnly`, `nominalFamilies:["ROLL UP"]`, `moveExisting:false`, `gridNearDist:12000`, `logPath`. |
| `opening_dims_each` | **Dim từng cửa** (1V): `audit` liệt kê cửa thiếu dim đứng/ngang theo tham chiếu; `apply` + `openingId` bổ sung đúng phần thiếu cho một cửa. |
| `dims_declutter` | Dời dim (theo type) sang ngang từng bước 1.2 mm để hết chồng chữ/tag/cửa/sàn cắt. |
| `opening_vdims` | Dim đứng cho từng cửa chỉ định. |
| `rollup_dims_add` | Cửa cuốn theo TOP/LEFT/RIGHT; `fixChains` dựng lại chuỗi. |
| `dedupe_new_vdims` | Xoá dim đứng mới trùng giá trị với dim có sẵn. |
| `dims_split` | Tách chuỗi tại đoạn dài hơn `maxMm`. |
| `dims_edit` | Xoá / gộp (merge) / đổi type (retype) dim, có log. |
| `dim_restore` | Dựng lại dim từ stable reference đã log. |
| `dim_type_check_copy` / `swap_dim_type` | Tạo type kiểm tra / đổi type hàng loạt. |

## Tag và title

| Lệnh | Việc |
|---|---|
| `elevation_opening_tags` | Tag cửa trên mặt đứng/cắt (1V), `addLeader`. |
| `tag_align` | Căn đầu tag theo tag mẫu. |
| `room_tags_outside` / `room_tags_broken` | Room tag nằm ngoài phòng / room tag "?". |
| `viewport_titles_place` | Bật và căn title (`ignoreCrop`). |

## View và trục

| Lệnh | Việc |
|---|---|
| `set_viewport_type` | Đổi viewport type (preview / apply / undo). |
| `crop_side_to_grids` | Kéo một cạnh crop tới sát trục ngoài cùng. |
| `align_grid_ends` | Căn đầu trục 2D (`compact`). |
| `grid_bubble_elbow` | Elbow cho bubble (hay lỗi → dùng so le đầu trục). |
| `view_image_trial` | Thử ẩn/bỏ template rồi xuất ảnh, luôn rollback. |
