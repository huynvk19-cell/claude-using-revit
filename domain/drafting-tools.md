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
- Khi không chắc tham số: đọc header của file (`/* mcp-tool */`) và comment `// ---- Details` ngay dưới nó (giải thích đầy đủ).
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
| `stair_plan_audit` | **Lõi thang bộ** (1V, mặt bằng, `drafting-stair-plan.md`): vế V1/V2/V3 theo mặt cắt, số bậc tính (bậc trên cùng ngang chiếu nghỉ không tính) + công thức, bề rộng thông thuỷ (mép tay vịn → mép bậc → tường), tường bao 4 phía, chiếu nghỉ (đầu tay vịn giữa → tay vịn/lan can chắn → tường, theo nét tay vịn đang hiện); dim cần có (`Expected`: OK / thiếu / thiếu CLEAR / CLEAR sai chỗ / sai công thức), tag, ô mã hoàn thiện (Generic Annotation, `FinishMarks`), cao độ, stair path, số bậc; type dùng nhiều nhất trong dự án. |
| `stair_views_survey` | Các view mặt bằng có tên chứa một chuỗi: đếm chú thích theo category + type, sheet chứa view. Dùng để xem các view cùng loại đã xong của dự án làm gì. |
| `finish_marks_survey` | Ô mã hoàn thiện (Generic Annotation tên có FINISH) trên các view theo tên: mã (F13, W05…) và số lượng. |
| `room_at_point` | Room tại các điểm khung view (+1 m trên level) và tham số chứa "Finish" (Wall / Floor Finish…); tìm room theo tên. |
| `number_systems_info` | Số bậc (NumberSystem) trong các view: run, type, mọi tham số (Display Rule, Reference…). |
| `view_cat_hidden` | Category / subcategory nào đang ẩn trong 1 view (vd. `<Above> Top Rails`). |
| `dims_by_type_inventory` | Mọi dim của 1 type (hoặc ids): view, sheet, giá trị, vị trí đường dim, chữ bị dời. |
| `opening_tags_in_views` | Phần tử (cửa…) có được thấy trong view không và có tag nào trỏ vào không. |
| `door_window_tag_check` | Kiểm tag cửa đi / cửa sổ trong view: thiếu, hỏng, trùng, bị nét đè (audit / sửa). |
| `annotation_override_scan` | Chú thích còn bị Override màu nét (vd. tô đỏ khi soát chưa xoá) trên các view của sheet theo tiền tố. |
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
| `stair_plan_annotate` | Lõi thang bộ (1V): stair path Fixed Up Direction, tắt chữ UP/DOWN (SD); số bậc từng vế, V1 phía đối diện V3 (C, `numberSide`); tag vế thang ngoài tường bên, leader vào phần nhìn thấy (SB1, `runTagPlace`). preview / apply / undo. Truyền `pathTypeName` / `runTagTypeName` theo bản vẽ cùng sheet. |
| `stair_numbers_match` | Số bậc (1V): chép Display Rule, Number Size, Justify, Orientation… từ một số bậc mẫu của dự án (`sourceId`) và đặt Reference từng vế (Left/Right Quarter…). preview / apply / undo. |
| `dims_at_positions` | Dim (1V) đặt theo toạ độ khung view (như `stair_plan_audit`): mỗi vị trí phải có tham chiếu thật (mặt/nét tường, lan can, vế thang – fallback mặt 3D, sàn, cửa, cột, trục); `{mm, id}` ép đúng phần tử; không tìm thấy → báo các tham chiếu gần nhất. `typeName` bắt buộc. `src:"3d"|"view"` theo từng điểm, `use3D`, `refDims` (dùng lại tham chiếu của dim vẽ tay). Bỏ nét thuộc subcategory bị ẩn. preview / apply / undo. **Sau khi tạo kiểm tra dim có hiện không** (`view_elem_boxes`: Box ≠ null). |
| `annot_place` | Tag (có/không leader, đầu leader tự do) và cao độ (spot, trên mặt plan của phần tử chứa điểm, vd. Stairs) đặt theo toạ độ khung view; type phải có sẵn. preview / apply / undo. |
| `dims_text_move` | Dời chữ của từng đoạn dim (dọc / ngang đường dim) khi chữ đoạn ngắn đè nhau. preview / apply / undo. |
| `move_in_view` | Dời phần tử chú thích (dim, tag, text) theo Right / Up của view (mm). |
| `view_elem_boxes` | (read-only) Hộp bao của phần tử trong view, theo mm model và khung view (ViewBox); `allAnnotations:true` = mọi chú thích của view. Box `null` = không được vẽ. |
| `view_dims_snapshot` | (read-only) Mọi dim tuyến tính của 1 view theo khung view: vị trí đường dim, các điểm gióng, giá trị + prefix/suffix/below từng đoạn, tham chiếu. Chụp trước / sau khi user chỉnh tay để học cách user dim (`outPath`). |
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
| `tag_leaders_info` | (RO) Tag + leader trong view: đầu tag, điểm gấp, điểm cuối, hình dạng (V / H / V+H / H+V / D xiên). |
| `view_range_info` | (RO) View range (top / cut / bottom / view depth, cao độ tuyệt đối) + cao độ Z của phần tử. |
| `dim_stable_refs` | (RO) Stable reference của từng tham chiếu trong dim + id / UniqueId Top Rail của lan can. |
| `number_systems_copy` | Chép thiết lập số bậc (Display Rule, Number Size, Justify…) từ một tread number chuẩn sang các tread number khác, giá trị lưu sẵn (không parse đơn vị). |
| `dims_rail_refs` | Dim bám tay vịn mà Revit VẼ ĐƯỢC: dựng tham chiếu như khi pick tay (`<UniqueId top rail>:1:INSTANCE:<cạnh symbol>:LINEAR`), tự thử từng cạnh bằng dim thử tới trục (giá trị đúng + có Box). Vị trí: `{mm, rail}` / `{mm, wall}` / `{mm, ref}` / `{mm, dimId, index}`; `mode probe` liệt kê cạnh. Tay vịn nằm trên mặt cắt (<Above>) không dim được. |
| `dims_copy_refs` | Chép dim sang view khác bằng chính tham chiếu của dim nguồn (stable representation): dùng dim vẽ tay bám tay vịn (Top Rail) mà API không tham chiếu được để Revit vẫn vẽ; giữ prefix/suffix; preview báo dim có được vẽ không. |
| `column_regions_survey` | (RO) Filled region trong view + cột (Structural Columns/Columns, host + link) bị mặt cắt view cắt qua: mặt cắt cột tại cut plane, cao độ đáy/đỉnh. |
| `column_regions_sync` | Vẽ lại filled region cột theo mặt cắt cột thật tại cut plane (preview / apply): loop khớp giữ, loop lệch thay; `regionTypes` lọc type; `dropUnmatched` bỏ loop không còn cột (cột dừng dưới cut plane); `addMissing` thêm cột chưa có. Tạo lại region (giữ type, override, Comments), bỏ qua region có dim bám. |
