---
name: drafting-api-pitfalls
description: "Các bẫy Revit API (2023) đã gặp khi tự động hoá triển khai bản vẽ, kèm cách tránh. Revit API pitfalls for drafting automation."
metadata:
  updated: "2026-10-05"
  related: ["drafting-tools", "drafting-dimensions", "drafting-views-sheets"]
---

# Bẫy Revit API (Pitfalls)

| Hiện tượng | Nguyên nhân | Cách làm |
|---|---|---|
| Cửa vẫn thiếu dim sau khi chạy tool theo hàng | Tool tính "đã có dim" theo vị trí (dim nào gần cửa cũng tính), và bỏ qua cả hàng khi hết chỗ. | Kiểm tra theo **tham chiếu** từng cửa (`opening_dims_each audit`), bổ sung từng cửa một. |
| Cao độ level lệch so với cửa khi so sánh | `Level.Elevation` là tuyệt đối, còn toạ độ cửa tính theo gốc view. | Đổi level sang toạ độ view: `VY(new XYZ(O.X, O.Y, level.ProjectElevation))`. |
| Revit "Not Responding" khi chạy trên mặt đứng/mặt cắt | Gọi nhiều view một lần. Lệnh động không ngắt được từ bên ngoài. | Mỗi lần gọi một view, đặt `timeoutSeconds`, kiểm tra `Responding` trước khi gọi. |
| Dựng lại dim thì **mất reference grid** (vd. 31 → 25 đoạn) | Reference grid lấy từ dim cũ hoặc từ `ParseFromStableRepresentation` bị Revit bỏ. | Với grid luôn dùng `new Reference(grid)`. |
| Reference Level bị bỏ khi gộp với reference của instance khác | Revit loại reference không cùng mặt phẳng hoặc không hợp lệ. | Dùng 2 dim thẳng hàng (Level→Bottom và chiều cao khung) thay vì một chuỗi. |
| Cửa cuốn dim ra 3400 thay vì 3000 | Lấy mặt khung / hộp cuốn. | Dùng named reference của family (`GetReferenceName` = TOP / LEFT / RIGHT). |
| Dim hoặc tag cho cửa bị che | Bounding box không biết vật che phía trước. | `ReferenceIntersector` nhiều tia, có `FindReferencesInRevitLinks`; tia trúng vật khác trước thì coi là bị che. |
| Không tìm thấy dim đen trong dependent view | Dim thuộc view cha (`OwnerViewId` ≠ dependent). | Thu thập dim bằng `FilteredElementCollector(doc, viewId)`, không lọc theo `OwnerViewId`. |
| Dim trục không hiện ở dependent | Dim nằm ngoài crop của dependent. | Đặt dim vào trong crop của từng dependent. |
| Sửa crop không có tác dụng | View gắn Scope Box. | Chỉnh scope box bằng tay. |
| Đọc `d.Id` sau `RollBack` bị lỗi | Phần tử không còn tồn tại. | Lấy id trước khi rollback. |
| `grid_bubble_elbow` báo "leader not valid" | Hình học bubble hoặc leader không hợp lệ ở tỉ lệ đó. | So le đầu trục 2D thay cho elbow. |
| Đổi hiển thị link trong template | Revit 2023 không có `SetLinkOverrides`. | Điều khiển UI: View Templates › V/G › Revit Links › Display Settings (bấm lần 1 để chọn, lần 2 để mở). Sau mỗi lần OK, Revit regenerate rất chậm. |
| Room tag báo chồng lắp khắp nơi | Bounding box của room tag phủ cả khung label của family, rộng hơn chữ nhiều (~29 mm giấy ở 1:200). | Không dò room tag bằng bounding box (`annotation_overlaps` mặc định bỏ qua); soát room tag bằng ảnh. |
| Chữ dim tiếng Việt trong script PowerShell bị lỗi | PowerShell 5.1 đọc file không có BOM. | Lưu `.ps1` dạng UTF-8 with BOM. |

## Mẹo (Tips)

- Lệnh có `mode: preview` → chạy trong transaction rồi rollback, trả về giá trị dự kiến. Đọc kỹ trước khi apply.
- Log `apply` cần giữ lại các id đã tạo hoặc xoá, và stable reference của dim bị xoá, để `dim_restore` và `undo` dùng được.
- Kiểm tra bằng ảnh: script `crop.ps1` (System.Drawing) cắt vùng theo tỉ lệ ảnh: `-Cx -Cy -W -H` (0–1).
