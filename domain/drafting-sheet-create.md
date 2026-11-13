---
name: drafting-sheet-create
description: "Chuẩn khởi tạo sheet trong Revit từ sổ bộ hồ sơ: chỉ tạo sheet có trong sổ, gom bản vẽ thành sheet, số và tên sheet, khung tên, tạo qua danh mục bản vẽ để print set + No. đồng bộ, tham số sheet chung / riêng, nhóm trong Project Browser, kiểm sau khi tạo (SC1–SC10). Sheet creation standard."
metadata:
  updated: "2026-11-13"
  related: ["drafting-sheet-layout", "drafting-views-sheets", "drafting-tools"]
---

# Khởi tạo sheet (Sheet creation)

Skill: `drafting-sheet-create`. Đầu vào là sổ bộ hồ sơ (`drafting-sheet-layout.md` §11): các dòng ở trạng thái `planned`, đã có cột `Vừa khổ` / `Vùng` (thẻ 03 của `drafting-sheet-layout`). Đầu ra là cột `Số sheet`, `Tên sheet`, `Sheet id`, và trạng thái `sheet`.

## 1. Quy tắc

| Mã | Quy tắc |
|---|---|
| SC1 | **Chỉ tạo sheet có trong sổ.** Mỗi sheet tạo ra ứng với một hoặc nhiều dòng của sổ. Không tạo sheet "để sẵn" ngoài sổ. |
| SC2 | **Gom bản vẽ thành sheet** theo LO2 và LO10 (`drafting-sheet-layout.md` §6): một chủ đề và một tỷ lệ chính mỗi sheet, mật độ 40–85 %. Theo thói quen của user, mỗi tầng một sheet cho mặt bằng sàn, lát sàn và trần. Các dòng `ID.a`, `ID.b` của cùng một bản vẽ luôn nằm cùng một sheet. |
| SC3 | **Số sheet** theo HS3: tiền tố + nhóm + thứ tự. Số đi theo đúng thứ tự dòng của sổ (thứ tự của danh mục điển hình, nếu loại công trình có danh mục đó). Chừa số trống giữa các nhóm. |
| SC4 | **Tên sheet đầy đủ** theo HS6, TG1–TG5 (`drafting-sheet-layout.md` §8): in hoa, viết đủ chữ (không `MB`, `MC`, `WC`…), đúng tên bản vẽ trong sổ, kèm tầng / vùng / mẫu; sheet chi tiết bắt đầu bằng `CHI TIẾT` (`CHI TIẾT VỆ SINH 1`, `CHI TIẾT THANG BỘ 1`). Tên sheet phải trùng với tên ghi trong danh mục điển hình (vd. `MẶT BẰNG SÀN LẦU 1`), không tự đặt cách gọi khác. |
| SC5 | **Khung tên** = type của profile, một khổ cho cả bộ. Chỉ tạo khi khung tên đó đã có trên ít nhất một sheet của danh mục, vì lệnh dùng khung tên được nhiều sheet dùng nhất. Danh mục chưa có sheet nào, hoặc kết quả báo placeholder → dừng lại, xin user tạo sheet đầu tiên bằng tay với đúng khung tên. |
| SC6 | **Tạo qua danh mục bản vẽ** (`cur_task schedule_table_write`, `add`). Không tạo sheet ngoài danh mục: làm qua danh mục thì print set cùng tên và cột No. tự đồng bộ (`drafting-views-sheets.md` §5). Luôn chạy `dryRun` trước. |
| SC7 | **Tham số chung** của mọi sheet (tên dự án nếu là tham số sheet, giai đoạn, ngày phát hành, người vẽ / kiểm / duyệt…) lấy theo profile, điền bằng `cur_task sheet_list_fill`, mỗi tham số một lần cho cả danh mục. |
| SC8 | **Tham số riêng** của từng sheet (tham số nhóm Project Browser, mã mẫu nhà / hạng mục, vùng) điền bằng `cur_task schedule_table_write`, `changes`. Tên tham số lấy theo profile. Sau khi tạo, sheet phải nằm đúng nhóm trong Project Browser. |
| SC9 | **Không đụng sheet đã phát hành**: không đổi số, tên, khung tên, không xoá (HS3). Sheet thừa (`extra`) chỉ được báo cho user. |
| SC10 | **Kiểm sau khi tạo**: mọi dòng của sổ có `Sheet id`; danh mục và print set khớp nhau (`print_sets_status` = `inSync`); Project Browser cho thấy đúng tên và đúng nhóm (`sheet_browser_names`). |

## 2. Cách gom sheet theo loại bản vẽ (mặc định)

| Loại bản vẽ | Mỗi sheet chứa |
|---|---|
| Bìa, danh mục, ghi chú chung | 1 nội dung / sheet. Danh mục dài thì tách nhiều sheet theo thứ tự |
| Vị trí, định vị, tổng thể | 1 bản vẽ / sheet; nhà nhỏ khổ A3 có thể gộp vị trí + định vị |
| Mặt bằng sàn, lát sàn, trần | 1 tầng / sheet (theo vùng nếu có chia vùng) |
| Mặt bằng mái, sân thượng | 1 sheet (mái tum + sân thượng chung) |
| Mặt đứng | 2 mặt đứng / sheet, xếp trên dưới, cặp đối diện nhau (`drafting-sheet-layout.md` LO5) |
| Mặt cắt | 1–2 mặt cắt / sheet |
| Chi tiết thang bộ | 1 thang / sheet: mặt bằng các tầng + mặt cắt + chi tiết phóng lớn mặt cắt + chi tiết mũi bậc (CT2). Không vừa thì tách theo thứ tự `… - MẶT BẰNG`, `… - MẶT CẮT`, `… - CHI TIẾT` |
| Chi tiết thang máy | 1 thang / sheet: mặt bằng hố thang các tầng + mặt cắt hố (CT3) |
| Chi tiết vệ sinh | 1 loại vệ sinh / sheet: mặt bằng kích thước + mặt bằng lát sàn cùng hàng, triển khai tường hàng dưới (CT1). Không vừa thì tách `… - MẶT BẰNG` (đủ hai mặt bằng) và `… - TRIỂN KHAI TƯỜNG` |
| Chi tiết cấu tạo, chi tiết nhỏ | Lưới ô (LO9), nhiều chi tiết cùng chủ đề / sheet |
| Thống kê cửa, chi tiết cửa | Bảng thống kê 1 sheet; chi tiết cửa theo lưới ô |

## 3. Lệnh

| Việc | Lệnh |
|---|---|
| Tạo sheet | `cur_task {task:"schedule_table_write", args:{schedule, add:[{number, name, values}], dryRun}}` |
| Đổi tên / số sheet chưa phát hành, tham số riêng | `cur_task {task:"schedule_table_write", args:{schedule, changes:[{id, col, value}]}}`. Lấy `col` từ `schedule_table` |
| Tham số chung | `cur_task {task:"sheet_list_fill", args:{schedule, param, value}}` |
| Đọc lại | `cur_task schedule_sheets`, `cur_task print_sets_status`, `sheet_browser_names` |

**Chưa có**: chọn khung tên khi tạo sheet. Hiện lệnh lấy khung tên được nhiều sheet dùng nhất (SC5). Lệnh cần thêm tham số `titleBlock` thì mới tạo được sheet đầu tiên của một bộ mới.
