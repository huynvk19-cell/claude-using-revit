---
name: drafting-work-rules
description: "Quy tắc làm việc khi Claude triển khai bản vẽ Revit như một kiến trúc sư triển khai (drafting architect): giới hạn quyền, nhịp làm việc, báo cáo. Hard rules and working loop."
metadata:
  updated: "2026-10-14"
  related: ["drafting-dimensions", "drafting-annotation", "drafting-views-sheets", "drafting-tools", "drafting-api-pitfalls"]
---

# Quy tắc làm việc — Drafting trên Revit

Các giá trị riêng của từng dự án (dim type, model cấm chạm, tiền tố sheet…) nằm trong `drafting-profile.md` ở thư mục gốc của dự án. Mẫu: `templates/drafting-profile.md`.

## 1. Luật cứng (Hard rules)

| # | Luật | Ghi chú |
|---|---|---|
| R1 | **Chỉ 2D / annotation** | Được sửa: dim, tag, room tag (vị trí), view title, viewport, 2D extent của grid/level, crop, view/template setting. **Không** sửa: wall, door, floor, room (Name/Number), 3D extent, link, model line. |
| R2 | **Không sync** | Chỉ sync khi user yêu cầu trong chính lượt đó. |
| R3 | **Không động vào model bị cấm** | Danh sách ở profile. |
| R4 | **Không có tên Claude/AI trong model** | Áp dụng cho tên type (cả hậu tố type đánh dấu), tham số, text, comment khi sync. |
| R5 | **Làm theo đợt: tối đa 10 view cùng loại** | Cùng loại = cùng ViewType của Revit (Floor Plan, Ceiling Plan, Section, Elevation…) và cùng chủ đề. Mỗi bước chạy cho cả đợt rồi mới sang bước sau. Mặt đứng / mặt cắt: lệnh vẫn **mỗi lần gọi một view**, kèm `timeoutSeconds` (gọi nhiều view một lúc → Revit treo). Chi tiết §2c. User, 2026-10-14. |
| R6 | **Gặp "dừng" / "tạm dừng" → dừng ngay** | Không chạy thêm lệnh nào. Báo đang dừng ở đâu. |
| R7 | **Ẩn bằng View Template, không dùng Hide in View** | Hide in View chỉ khi không còn cách khác, và phải nói rõ. |
| R8 | **Không bịa số liệu** | Mọi id, số đo, tên trong báo cáo phải lấy từ kết quả tool của lượt hiện tại. |
| R9 | **Không dùng "Replace with text" trên dim** | Giá trị dim luôn sống; chữ thêm vào Prefix / Suffix / Above / Below (`dims_text`). User, 2026-10-08. Chi tiết: `drafting-dimensions.md` §1b. |
| R10 | **Mọi phần tử tạo mới dùng type đánh dấu** | Type đánh dấu = `<tên type chính thức><hậu tố>` (hậu tố trong profile). Trả về chính thức chỉ khi user duyệt: Select All Instances → In Entire Project → chọn type chính thức, hoặc `mark_types restore`. Chi tiết §2c. User, 2026-10-14. |

Việc cần sửa model (vd. room không có tên → tag "?") → ghi vào **việc tồn** để user tự làm.

## 2. Nhịp làm việc (Working loop)

1. **Re-anchor**: gọi `get_active_view` hoặc tra lại id view. Không dùng lại id từ lượt trước.
2. **Cửa sổ trạng thái** (nếu có): show → update từng bước → close cuối lượt.
3. **Preview → Apply**:
   - Chưa chắc thì chạy `preview` trước.
   - `apply` luôn kèm `logPath`.
4. **Phần tử mới dùng type đánh dấu** (R10, §2c) cho tới khi user duyệt. Lệnh nhận tên dim type → truyền **type kiểm tra** = type đánh dấu của dim type chính thức.
5. **Kiểm tra bằng ảnh**: xuất ảnh các view của đợt trong một lần gọi → subagent đọc → sửa → xuất lại view bị sửa.
6. **Ghi log**:
   - `review/<ngày>_<mã>/<Txx>-*.json`
   - một mục trong `log/YYYY-MM.md`
7. **Bài học mới** từ chỗ user sửa:
   - Thêm một dòng vào file domain tương ứng nếu áp dụng cho mọi dự án.
   - Thêm vào profile nếu chỉ riêng dự án này.

## 2b. Tiêu chí hoàn thành: ĐỦ – ĐÚNG – ĐẸP (user, 2026-10-07)

Một bản vẽ chỉ được báo "xong" khi đạt cả ba. Kiểm tra trước khi báo cáo, cho từng view của đợt.

| | Yêu cầu | Cách kiểm |
|---|---|---|
| **ĐỦ** | Mọi mục của chuẩn (dim, tag, cao độ, hoàn thiện, path, số bậc…) đều có trên view. **Không bỏ qua mục nào im lặng**: mục không làm được → hỏi user hoặc ghi rõ vào "Việc tồn" kèm lý do. Không tự kết luận "dự án không dùng" khi chưa xem bản vẽ cùng loại của dự án. | Audit của chuẩn (vd. `stair_plan_audit`) không còn `missing`, trừ mục user đã duyệt bỏ. Đối chiếu với view cùng sheet đã hoàn chỉnh. |
| **ĐÚNG** | Số liệu lấy từ model (không đếm bằng mắt, không bịa). Dim bám hình học thật, đúng mép theo chuẩn (vd. thông thuỷ: lan can → mép bậc/chiếu nghỉ → tường hoàn thiện). Giá trị khớp tính toán (± 1 mm). Mã/tên đúng type, đúng tham số. Mọi dim/tag **thật sự được vẽ** (Box ≠ null, thấy trên ảnh). | Audit `OK`; `view_elem_boxes` (Box ≠ null); ảnh view. |
| **ĐẸP** | Không chữ/tag/dim đè nhau hay đè nét chính; các dòng dim cách đều (7 mm giấy), thẳng hàng; chữ đoạn ngắn được tách ra; leader ngắn, không cắt chữ; tag gần chủ thể; cùng kiểu trình bày với các view đã có trên cùng sheet. | `annotation_overlaps` = 0; ảnh view (xuất **view**, không xuất sheet). |

## 2c. Làm theo đợt và type đánh dấu (user, 2026-10-14)

**Đợt (batch)**
- Không làm xong từng view rồi báo. Gom tối đa **10 view cùng loại** (cùng ViewType, cùng chủ đề) thành một đợt; nhiều hơn → chia nhiều đợt. Không trộn mặt bằng với mặt cắt trong một đợt.
- Mỗi bước của skill chạy cho cả đợt: khảo sát cả đợt → preview cả đợt → apply cả đợt → kiểm tra cả đợt → **một** báo cáo cho đợt.
- Lệnh nhận `viewIds`: mặt bằng → một lần gọi cho cả đợt; mặt đứng / mặt cắt → mỗi lần một view (R5). Lệnh **1V** (một `viewId`): gọi lần lượt từng view của đợt, không dừng giữa chừng để báo.
- Một view lỗi (timeout, lỗi lệnh) → ghi lại, làm tiếp các view còn lại, báo cuối đợt.

**Type đánh dấu (mark type)**
- Mọi phần tử Claude **tạo mới** (dim, spot, tag, room tag, stair path, filled region, generic annotation, text…, kể cả dim dựng lại khi tách / gộp) mang type `<tên type chính thức><hậu tố>`. Hậu tố và màu lấy từ profile; tên trung tính (R4).
- Để làm gì: user nhìn là biết phần nào mới, và trả về chính thức chỉ bằng Select All Instances (In Entire Project) → đổi sang type chính thức.
- Cách làm mỗi đợt:
  1. Đầu đợt: `mark_types {action:"snapshot", viewIds:[≤10], statePath:review/<ngày>_<mã>/batch-<n>-snapshot.json}`.
  2. Lệnh cần tên dim type → `mark_types {action:"ensure", suffix, types:[<dim type chính thức>]}` một lần, rồi truyền tên type đánh dấu (= type kiểm tra).
  3. Sau apply: `mark_types {action:"mark", suffix, statePath, mode:"preview"}` → `apply` kèm `logPath`. Phần tử đã ở type đánh dấu được bỏ qua, nên chạy lại sau mỗi lần sửa; luôn chạy lần cuối trước khi báo cáo.
- Không đánh dấu được (ghi vào báo cáo): detail line (chỉ có line style), sửa phần tử có sẵn (dời tag, đầu trục 2D, crop) → chỉ có trong log `apply`.
- Trả về chính thức chỉ khi user duyệt: `mark_types {action:"restore", suffix, mode:"preview"}` → `apply` (logPath; `viewIds` để giới hạn; `deleteEmpty` xoá type đánh dấu không còn dùng). Kiểm tra còn lại: `mark_types {action:"list", suffix}`.

## 3. Báo cáo (Report)

- Viết bằng ngôn ngữ của user.
- Dùng **tên Revit chính xác**:
  - Sheet: `SỐ SHEET – TÊN SHEET`
  - View: đúng như trong Project Browser
  - View Template: `"tên"`
  - Category: tên tiếng Anh của Revit
- Mã việc ghi dạng `A(1)`, `T05`. **Không** dùng `A1`, `A2` vì dễ trùng với mã CLASSIFICATION của sheet.
- Nội dung chính là bảng: việc → sheet/view → đã làm gì → số lượng.
- Luôn kết thúc bằng hai mục:
  - **Cần xem**: chỗ chưa chắc, chỗ chồng chữ.
  - **Việc tồn**: việc phải làm tay, việc phải sửa model, số phần tử còn ở type đánh dấu (`mark_types list`).
