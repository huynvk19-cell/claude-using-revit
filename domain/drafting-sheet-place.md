---
name: drafting-sheet-place
description: "Chuẩn đặt view lên sheet từ sổ bộ hồ sơ: điều kiện trước khi đặt, bảng bố trí (toạ độ mm trên sheet), thứ tự đặt, căn chuỗi mặt bằng theo điểm trục, căn hàng mặt đứng / mặt cắt, legend / key plan / schedule, viewport type, kiểm sau khi đặt (VP1–VP10); lệnh có / chưa có. Viewport placement standard."
metadata:
  updated: "2026-11-11"
  related: ["drafting-sheet-layout", "drafting-sheet-create", "drafting-view-create", "drafting-views-sheets"]
---

# Đặt view lên sheet (Viewport placement)

Skill: `drafting-sheet-place`.
- **Đầu vào**: sổ bộ hồ sơ (`drafting-sheet-layout.md` §11), các dòng ở trạng thái `view` (đã có `Sheet id` và `View id`).
- **Đầu ra**: cột `Vị trí (mm)`, `Viewport id`, `Viewport type`, và trạng thái `placed`.
- Quy tắc bố cục là LO1–LO12 và Z2–Z3 (`drafting-sheet-layout.md` §6, §7). File này nói **cách đặt**.

## 1. Quy tắc

| Mã | Quy tắc |
|---|---|
| VP1 | **Chỉ đặt dòng đã sẵn sàng**: dòng ở trạng thái `view`, sheet đã tồn tại, view chưa nằm trên sheet khác (LO11). |
| VP2 | **Chốt kích thước view trước khi đặt**: crop, annotation crop và đầu trục 2D đã xong (`drafting-views-sheets` thẻ 02, 03), tỷ lệ đúng sổ. Đổi crop sau khi đặt sẽ làm lệch vị trí: phải lập lại bảng bố trí cho sheet đó. |
| VP3 | **Bảng bố trí trước, đặt sau**. Mỗi sheet có bảng: view → điểm đặt (mm từ góc dưới trái của sheet) → hộp trên giấy (rộng × cao mm). Bảng phải đạt LO1, LO6, LO10 trên giấy rồi mới được đặt. |
| VP4 | **Thứ tự đặt trên một sheet**: phần cố định trước (cột ghi chú, legend, key plan, mũi tên Bắc, LO8), rồi view chính, rồi các view phụ theo thứ tự đọc (LO3). |
| VP5 | **Chuỗi mặt bằng** (các tầng, các loại MB sàn / lát / trần, từng vùng): đặt theo **điểm trục gốc**, không theo tâm hộp. Giao của trục gốc mỗi phương (theo profile, mặc định trục đầu tiên: 1 và A) phải nằm cùng một điểm (mm) trên mọi sheet của chuỗi (LO4). Tâm hộp viewport đổi khi chú thích đổi nên không dùng để căn. |
| VP6 | **Mặt đứng / mặt cắt cùng sheet**: ±0.000 trên cùng đường ngang khi xếp cạnh nhau; trục cùng tên thẳng cột khi xếp trên dưới (LO5). |
| VP7 | **Viewport type** theo profile ngay khi đặt (có title, đúng kiểu); `drafting-views-sheets` thẻ 01 kiểm lại. Title đặt bằng `drafting-tags-titles` thẻ 01 sau khi đặt xong. |
| VP8 | **Legend dùng chung** (ký hiệu, key plan, chi tiết cửa) được đặt trên nhiều sheet. **Schedule** đặt ở cột phải hoặc chiếm một ô của lưới (LO9), không đè khung tên. |
| VP9 | **Không đụng phần của người khác**: không đặt view lên sheet đã phát hành khi chưa hỏi; không dời viewport người khác đặt mà không báo rõ viewport nào, dời bao nhiêu mm. |
| VP10 | **Kiểm sau khi đặt**: mọi dòng `view` → `placed`, có `Viewport id`; vị trí khớp bảng bố trí ±1 mm trên giấy (điểm trục với chuỗi mặt bằng); không viewport nào ra ngoài vùng vẽ hay chồng nhau (ảnh sheet). |

## 2. Cách tính điểm đặt

- **Hộp trên giấy** = (crop + annotation crop, m) × 1000 / tỷ lệ, cộng chỗ cho title bên dưới (theo viewport type).
- **View lẻ**: điểm đặt = tâm của ô dành cho view trong vùng vẽ.
- **Chuỗi mặt bằng**:
  1. chọn điểm giấy `P` cho giao trục gốc trên sheet đầu chuỗi, sao cho hộp của view lớn nhất trong chuỗi nằm gọn trong vùng vẽ;
  2. mọi sheet sau dùng đúng `P`.
- **Lưới ô (chi tiết)**: chia vùng vẽ thành ô đều; mỗi view nằm giữa ô, căn đỉnh theo hàng.

## 3. Lệnh

**Đọc và chỉnh**
| Việc | Lệnh |
|---|---|
| Đọc view / viewport / type trên sheet | `sheet_views_ids` (`includeAll` để thấy cả legend, schedule) |
| Đổi viewport type | `set_viewport_type` (preview / apply / undo) |
| Title | `viewport_title_audit`, `viewport_titles_place` |
| Ảnh sheet để kiểm | `export_sheet_images` |

**Lệnh `viewport_place`** (đặt view, legend, schedule lên sheet; chi tiết ở phần `// ---- Details` của `revit-commands/viewport_place.cs`):
- Toạ độ giấy tính bằng mm từ **góc dưới trái của khung tên** trên sheet.
- `items[]`, mỗi item gồm:
  - `sheet` (số sheet hoặc id), `viewId`;
  - vị trí: `centerMm {x, y}` (tâm hộp viewport, không tính title), **hoặc** `gridPoint {gridA, gridB, atMm {x, y}}` (giao 2 trục rơi đúng điểm giấy, VP5), **hoặc** `gridPoint {gridA, level, atMm}` (mặt đứng / mặt cắt: trục tại cao độ level, VP6);
  - `viewportType`;
  - `move: true` để dời viewport đã có trên sheet đó thay vì đặt mới.
- Chung cho cả lần gọi: `viewportType` mặc định, `areaMm {minX, minY, maxX, maxY}` (vùng vẽ của profile), `me` (tên user Revit: viewport người khác sửa lần cuối không bị dời, trả về `Ask`, VP9).
- `mode`: `preview` | `apply` | `undo`; `logPath`. `preview` làm thật rồi huỷ, nên hộp và va chạm là số thật.
- Kết quả: `Ids` theo thứ tự item, `Problems` gồm `Error` (view đang ở sheet khác thì nêu số sheet, VP1), `Reused`, `Ask`, `Outside` (mm vượt vùng vẽ theo [trái, dưới, phải, trên]), `Overlaps` (id viewport / schedule bị chồng). Hộp trên giấy từng item: `verbose` hoặc `outPath`.
- Không làm: mũi tên Bắc và các ký hiệu khác (không phải view); title (dùng `viewport_titles_place`).

`Outside` / `Overlaps` nghĩa là bảng bố trí sai: sửa bảng (VP3) rồi đặt lại, không dời bằng mắt.
