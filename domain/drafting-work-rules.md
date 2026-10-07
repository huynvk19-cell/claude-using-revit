---
name: drafting-work-rules
description: "Quy tắc làm việc khi Claude triển khai bản vẽ Revit như một kiến trúc sư triển khai (drafting architect): giới hạn quyền, nhịp làm việc, báo cáo. Hard rules and working loop."
metadata:
  updated: "2026-10-05"
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
| R4 | **Không có tên Claude/AI trong model** | Áp dụng cho tên type, tham số, text, comment khi sync. |
| R5 | **Mặt đứng / mặt cắt: mỗi lần gọi một view** | Luôn kèm `timeoutSeconds`. Gọi nhiều view một lúc → Revit treo. |
| R6 | **Gặp "dừng" / "tạm dừng" → dừng ngay** | Không chạy thêm lệnh nào. Báo đang dừng ở đâu. |
| R7 | **Ẩn bằng View Template, không dùng Hide in View** | Hide in View chỉ khi không còn cách khác, và phải nói rõ. |
| R8 | **Không bịa số liệu** | Mọi id, số đo, tên trong báo cáo phải lấy từ kết quả tool của lượt hiện tại. |

Việc cần sửa model (vd. room không có tên → tag "?") → ghi vào **việc tồn** để user tự làm.

## 2. Nhịp làm việc (Working loop)

1. **Re-anchor**: gọi `get_active_view` hoặc tra lại id view. Không dùng lại id từ lượt trước.
2. **Cửa sổ trạng thái** (nếu có): show → update từng bước → close cuối lượt.
3. **Preview → Apply**:
   - Chưa chắc thì chạy `preview` trước.
   - `apply` luôn kèm `logPath`.
4. **Dim mới dùng type kiểm tra** (theo profile) cho tới khi user duyệt.
5. **Kiểm tra bằng ảnh**: xuất sheet → cắt vùng → xem.
6. **Ghi log**:
   - `review/<ngày>_<mã>/<Txx>-*.json`
   - một mục trong `log/YYYY-MM.md`
7. **Bài học mới** từ chỗ user sửa:
   - Thêm một dòng vào file domain tương ứng nếu áp dụng cho mọi dự án.
   - Thêm vào profile nếu chỉ riêng dự án này.

## 2b. Tiêu chí hoàn thành: ĐỦ – ĐÚNG – ĐẸP (user, 2026-10-07)

Một bản vẽ chỉ được báo "xong" khi đạt cả ba. Kiểm tra trước khi báo cáo, mỗi view một lần.

| | Yêu cầu | Cách kiểm |
|---|---|---|
| **ĐỦ** | Mọi mục của chuẩn (dim, tag, cao độ, hoàn thiện, path, số bậc…) đều có trên view. **Không bỏ qua mục nào im lặng**: mục không làm được → hỏi user hoặc ghi rõ vào "Việc tồn" kèm lý do. Không tự kết luận "dự án không dùng" khi chưa xem bản vẽ cùng loại của dự án. | Audit của chuẩn (vd. `stair_plan_audit`) không còn `missing`, trừ mục user đã duyệt bỏ. Đối chiếu với view cùng sheet đã hoàn chỉnh. |
| **ĐÚNG** | Số liệu lấy từ model (không đếm bằng mắt, không bịa). Dim bám hình học thật, đúng mép theo chuẩn (vd. thông thuỷ: lan can → mép bậc/chiếu nghỉ → tường hoàn thiện). Giá trị khớp tính toán (± 1 mm). Mã/tên đúng type, đúng tham số. Mọi dim/tag **thật sự được vẽ** (Box ≠ null, thấy trên ảnh). | Audit `OK`; `view_elem_boxes` (Box ≠ null); ảnh view. |
| **ĐẸP** | Không chữ/tag/dim đè nhau hay đè nét chính; các dòng dim cách đều (7 mm giấy), thẳng hàng; chữ đoạn ngắn được tách ra; leader ngắn, không cắt chữ; tag gần chủ thể; cùng kiểu trình bày với các view đã có trên cùng sheet. | `annotation_overlaps` = 0; ảnh view (xuất **view**, không xuất sheet). |

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
  - **Việc tồn**: việc phải làm tay, việc phải sửa model, dim còn ở type kiểm tra.
