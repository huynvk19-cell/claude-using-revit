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
