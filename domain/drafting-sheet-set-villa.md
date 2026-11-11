---
name: drafting-sheet-set-villa
description: "Bộ hồ sơ kiến trúc biệt thự (đơn lập, song lập, nhà vườn, nghỉ dưỡng): bản vẽ cần có theo giai đoạn, tỷ lệ, mặt đứng 4 hướng, mái dốc, sân vườn, hồ bơi, cổng tường rào, phào chỉ, cách xếp sheet, hay sót. Villa sheet set."
metadata:
  updated: "2026-11-11"
  related: ["drafting-sheet-layout"]
---

# Bộ hồ sơ: biệt thự (Villa)

File này có **danh mục điển hình** (§2) thay cho bộ hồ sơ lõi (`drafting-sheet-layout.md` §4). GĐ, nhóm và các mã LO / Z dùng như trong file đó.

## 1. Đặc điểm ảnh hưởng tới hồ sơ

- **Đứng độc lập trên lô đất có sân vườn**:
  - nhìn thấy cả 4 hướng, nên phải có đủ mặt đứng mọi hướng;
  - song lập thì một hướng là tường chung.
- **Hình khối tự do**: lệch tầng, thông tầng, ban công và sân thượng nhiều cao độ.
- **Mái phức tạp**: mái dốc nhiều mặt, ngói, mái bằng kết hợp; sê nô, máng xối, diềm mái.
- **Ngoài nhà**: sân vườn, hồ bơi, cổng, tường rào, nhà bảo vệ, gara, chòi nghỉ, tiểu cảnh.
- **Phong cách**:
  - tân cổ điển: phào chỉ, cột, lan can con tiện, trần trang trí;
  - hiện đại: lam, kính lớn, mái đua.
- **Khu biệt thự dự án**: nhiều mẫu, mẫu đối xứng, song lập ghép đôi.

## 2. Danh mục điển hình (user, 2026-11-11)

Đây là danh mục **thay cho** bộ lõi khi job là biệt thự: các dòng của bộ lõi đã nằm sẵn trong bảng. Thứ tự các dòng chính là thứ tự sheet trong bộ hồ sơ.

- **Lập cho giai đoạn thi công (TC)**: ở TC mọi dòng đều bắt buộc. Job ở giai đoạn sớm hơn chỉ lấy các dòng có GĐ không muộn hơn giai đoạn của job (`drafting-sheet-layout.md` §2).
- **Cách gọi tầng**: theo profile. Dòng "mỗi tầng" khai triển thành một dòng cho mỗi tầng có thật trong model.
- **Cột Điều kiện**: dòng có điều kiện chỉ bắt buộc khi điều kiện đó đúng.

| # | Nhóm | Bản vẽ | Tỷ lệ | GĐ | Điều kiện | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|---|---|
| 1 | 0 | Bìa (sheet riêng) | — | CS | — | Tên công trình, địa điểm, chủ đầu tư, đơn vị thiết kế, giai đoạn, ngày; không đặt danh mục trên bìa (user, 2026-11-11) | `cover` |
| 2 | 0 | Danh mục bản vẽ (sheet riêng) | — | CS | — | Theo `drafting-sheet-layout.md` §8 (cột, nhóm, STT); khớp print set (`drafting-views-sheets.md` §5) | `sheet-list` |
| 3 | 0 | Ghi chú chung, ký hiệu, chữ viết tắt | — | KT | — | Một bảng ký hiệu 5 nhóm theo `drafting-sheet-layout.md` §8b (KH1–KH7) | `notes` |
| 4 | 0 | Bảng vật liệu / hoàn thiện chung | — | KT | — | — | `schedule` |
| 5 | 1 | Tổng mặt bằng lô + sân vườn | 1:200–1:100 | CS | — | Chỉ giới, lộ giới, khoảng lùi; nhà, sân, lối vào xe, cổng, hồ bơi, cây xanh, cao độ sân, hướng Bắc | `site` |
| 6 | 1 | Mặt bằng định vị | 1:200–1:100 | KT | — | Toạ độ mốc, kích thước tới ranh | `site` |
| 7 | 1 | Mặt bằng hoàn thiện sân vườn | 1:100 | TC | — | Vật liệu lát, bồn cây, rãnh thoát nước, cao độ, độ dốc | `site` |
| 8 | 1 | Cổng, tường rào: mặt bằng, mặt đứng, mặt cắt | 1:50–1:20 | KT | — | — | `subset` |
| 9 | 1 | Nhà bảo vệ, gara, chòi nghỉ | 1:50 | KT | tách khối khỏi nhà chính | Bộ nhỏ: mặt bằng, mặt đứng, mặt cắt | `subset` |
| 10 | 2 | Mặt bằng mỗi tầng | 1:100–1:50 | CS | — | Trục, kích thước tổng, tên + diện tích phòng, cao độ sàn; lệch tầng ghi cao độ từng phần, phần thông tầng | `plan` |
| 11 | 2 | Mặt bằng mái | 1:100 | CS | — | Các mặt mái, độ dốc %, đỉnh / sống / khe mái, sê nô, máng xối, ống đứng | `roof` |
| 12 | 2 | Mặt bằng kích thước mỗi tầng | 1:100–1:50 | TC | — | — | `plan-dims` |
| 13 | 2 | Mặt bằng hoàn thiện sàn mỗi tầng | 1:100–1:50 | TC | — | — | `plan-finish` |
| 14 | 2 | Mặt bằng trần mỗi tầng (trần trang trí, giật cấp) | 1:50 | TC | — | Cao độ trần từng cấp, phào trần, đèn hắt | `ceiling` |
| 15 | 3 | Mặt đứng 4 hướng (song lập: 3) | 1:100–1:50 | CS | — | Cao độ đỉnh mái, vật liệu, ngói, phào | `elevation` |
| 16 | 4 | Mặt cắt ≥ 2 (dọc + ngang), qua thang và thông tầng | 1:50 | CS | — | Cao độ, độ dốc mái, chiều cao thông thuỷ | `section` |
| 17 | 4 | Mặt cắt qua mái phức tạp, qua lệch tầng | 1:50 | KT | — | — | `section` |
| 18 | 5 | Thang (thường là thang trang trí, thang xoắn): mặt bằng + mặt cắt | 1:25–1:20 | KT | — | Lan can, tay vịn, ốp bậc | `stair` |
| 19 | 6 | Vệ sinh, bếp, phòng thay đồ: mặt bằng phóng to + triển khai tường | 1:25–1:20 | TC | — | — | `enlarged` |
| 20 | 6 | Hồ bơi: mặt bằng, mặt cắt, chi tiết tràn và máng thu | 1:50–1:10 | KT | có hồ bơi | Cao độ nước, lối xuống, phòng máy | `subset` |
| 21 | 7 | Thống kê cửa + mặt đứng cửa | 1:50 | KT | — | — | `door` |
| 22 | 7 | Cửa, vách kính lớn, cửa trượt ra sân vườn | 1:50–1:20 | KT | — | — | `door` |
| 23 | 8 | Chi tiết cấu tạo: tường, sàn, mái, chống thấm, lan can | 1:20–1:5 | TC | — | — | `detail` |
| 24 | 8 | Chi tiết mái: diềm, sê nô, máng xối, ngói, khe mái, chống thấm | 1:20–1:5 | TC | — | — | `detail` |
| 25 | 8 | Chi tiết phào chỉ, cột, lan can, ban công | 1:10–1:2 | TC | phong cách tân cổ điển | Profile phào ở 1:2 hoặc 1:1 | `detail` |
| 26 | 8 | Chi tiết lam, mái đua, ô văng | 1:20–1:5 | TC | phong cách hiện đại | — | `detail` |

## 3. Layout đặc thù

- **Khổ giấy**: **A2** cho cả bộ, như nhà liên kế (user, 2026-11-11: cùng loại hình nhà ở thấp tầng). Profile chưa ghi khổ thì dùng A2; khổ khác chỉ khi user duyệt. View không vừa A2 → tỷ lệ nhỏ hơn kế tiếp trong khoảng cho phép (HS5), rồi tách sheet.
- **Vừa A2** (user, 2026-11-11, bộ mẫu biệt thự 2 tầng): mặt bằng, mái, mặt đứng 1:100 (2 mặt đứng / sheet); mặt cắt 1:50, một mặt cắt / sheet, crop ngang theo khối nhà ±1 m (không theo đầu trục), annotation crop 12 mm, đầu level / trục 2D kéo về sát crop; trần 1:50 chia vùng (Z1–Z5) khi một tầng không vừa; thang, vệ sinh, bếp 1:25, crop sát phòng / thang; mỗi WC một sheet khi 4 mặt triển khai không vừa cùng mặt bằng; tổng mặt bằng, định vị 1:200, hoàn thiện sân vườn 1:100 (crop theo khối nhà, không theo cả khu đất).
- **Mặt đứng 4 hướng**: thường 2 mặt đứng một sheet, xếp trên dưới. Trục thẳng hàng, cao độ đọc được ở cả hai (LO5). Mặt đứng đối diện nhau (trước / sau, trái / phải) để chung sheet.
- **Mặt bằng mái** đặt cùng sheet với mặt bằng tầng trên cùng, hoặc trên sheet riêng nhưng ở cùng vị trí (LO4).
- **Phào chỉ**: sheet chi tiết dạng lưới (LO9), mỗi ô một profile phào. Đặt mặt đứng thu nhỏ có đánh dấu vị trí phào ở ô đầu.
- **Ngoài nhà** (sân vườn, cổng, hồ bơi) nằm ở nhóm 1 và nhóm 6, không trộn vào sheet mặt bằng nhà.

## 4. Hay sót (kiểm khi lập danh mục)

- Thiếu mặt đứng hướng ít nhìn thấy (hướng hông, phía sau).
- Mặt bằng mái thiếu độ dốc, thiếu vị trí ống đứng / máng xối.
- Thiếu mặt cắt qua mái phức tạp, qua lệch tầng.
- Thiếu cổng, tường rào, nhà bảo vệ, hồ bơi.
- Thiếu chi tiết phào và mặt đứng chỉ vị trí phào.
- Song lập: thiếu chi tiết tường chung, khe lún giữa hai căn.
