---
name: drafting-sheet-set-public
description: "Bộ hồ sơ kiến trúc công trình công cộng: trường học, bệnh viện, trung tâm thương mại (cả văn hoá, thể thao): phân khu chức năng, phân luồng, phòng điển hình phóng to, tiếp cận người khuyết tật, thoát nạn, bản vẽ đặc thù từng loại; tỷ lệ, cách xếp sheet, hay sót. Public building sheet set."
metadata:
  updated: "2026-11-05"
  related: ["drafting-sheet-layout"]
---

# Bộ hồ sơ: công trình công cộng (Public buildings)

File này cộng thêm vào bộ hồ sơ lõi (`drafting-sheet-layout.md` §4). GĐ, nhóm và các mã LO / Z dùng như trong file đó.

Bảng chung (§1) áp dụng cho mọi công trình công cộng. Sau đó chỉ lấy **một** mục riêng (§2, §3 hoặc §4) theo loại trong profile. Công trình văn hoá, thể thao dùng §1 và hỏi user các bản vẽ đặc thù.

## 1. Chung cho công trình công cộng

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 0 | Bảng chỉ tiêu: quy mô (học sinh / giường / diện tích cho thuê), diện tích từng khu | — | CS | — | `schedule` |
| 2 | Mặt bằng phân khu chức năng mỗi tầng (màu + legend) | 1:500–1:200 | CS | — | `thematic` |
| 2 | Mặt bằng thoát nạn / PCCC mỗi tầng | 1:200 | CS | Khoang cháy, lối thoát, khoảng cách, cửa chống cháy | `thematic` |
| 2 | Mặt bằng tiếp cận người khuyết tật | 1:200 | KT | Lối vào, ram dốc, thang máy, vệ sinh người khuyết tật; theo QCVN 10 | `thematic` |
| 6 | Chi tiết vệ sinh công cộng điển hình + phòng người khuyết tật | 1:50–1:25 | TC | Hai mặt bằng: kích thước, lát sàn (CT1) | `enlarged` |
| 7 | Thống kê cửa (cửa thoát nạn, cửa chống cháy, chiều mở) | 1:50 | KT | — | `door` |

## 2. Trường học

**Đặc điểm**
- Các khối: khối lớp học, khối hiệu bộ / hành chính, nhà đa năng, thư viện, phòng bộ môn, bếp ăn (mầm non, bán trú).
- Phòng học lặp lại: dùng **phòng điển hình**.
- An toàn trẻ em: lan can cao, khe hở nhỏ, không có bậc hụt, cửa không kẹp tay.
- Tiêu chuẩn theo cấp học (mầm non, tiểu học, THCS, THPT).

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 1 | Tổng mặt bằng: sân chơi, sân thể thao, cổng, nhà xe, cây xanh | 1:500–1:200 | CS | — | `site` |
| 6 | Phòng học điển hình: mặt bằng bố trí bàn ghế, bảng, cửa sổ, hướng chiếu sáng; triển khai tường | 1:50 | KT | — | `enlarged` |
| 6 | Phòng bộ môn, thư viện, phòng đa năng | 1:50–1:100 | KT | — | `enlarged` |
| 6 | Chi tiết vệ sinh học sinh theo cấp (chiều cao thiết bị) | 1:25 | TC | Hai mặt bằng: kích thước, lát sàn (CT1) | `enlarged` |
| 6 | Bếp ăn (mầm non, bán trú): luồng sơ chế → nấu → chia | 1:50 | KT | — | `enlarged` |
| 8 | Chi tiết lan can hành lang, cầu thang (chiều cao, khe hở), tay vịn hai cao độ | 1:20–1:5 | TC | — | `detail` |

## 3. Bệnh viện

**Đặc điểm**
- Các khối khoa phòng: khám, cấp cứu, kỹ thuật nghiệp vụ, nội trú, hành chính, hậu cần.
- **Phân luồng** nghiêm ngặt: sạch / bẩn, bệnh nhân / nhân viên / khách, lây nhiễm.
- Phòng đặc thù:
  - phòng mổ, ICU, phòng sạch;
  - chẩn đoán hình ảnh (che chắn tia X bằng chì / bê tông);
  - xét nghiệm.
- Thang máy chở giường bệnh, ram dốc cho băng ca.
- Phối hợp khí y tế, thiết bị y tế.

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 2 | Mặt bằng phân luồng: sạch / bẩn, bệnh nhân / nhân viên / khách, cấp cứu (màu + legend) | 1:500–1:200 | CS | — | `thematic` |
| 6 | Phòng bệnh điển hình (theo loại): bố trí giường, đầu giường (khí y tế), vệ sinh | 1:50 | KT | — | `enlarged` |
| 6 | Khối phẫu thuật: phòng mổ, chuẩn bị, hồi tỉnh, luồng vô khuẩn | 1:50 | KT | — | `enlarged` |
| 6 | Chẩn đoán hình ảnh: mặt bằng + mặt cắt lớp che chắn bức xạ | 1:50–1:20 | TC | — | `enlarged` |
| 6 | ICU, phòng sạch, xét nghiệm: mặt bằng thiết bị | 1:50 | TC | — | `enlarged` |
| 6 | Mặt bằng bố trí thiết bị y tế (nền phối hợp) | 1:100–1:50 | TC | — | `thematic` |
| 5 | Thang máy giường bệnh, ram dốc băng ca (độ dốc) | 1:50 | KT | — | `vertical` |
| 8 | Chi tiết hoàn thiện kháng khuẩn: góc lượn chân tường, tay vịn hành lang, chống va đập | 1:10–1:5 | TC | — | `detail` |

## 4. Trung tâm thương mại

**Đặc điểm**
- Lưới cột lớn; giếng trời (atrium) nhiều tầng; thang cuốn.
- Gian hàng cho thuê: cửa hàng lớn (anchor), gian nhỏ, khu ăn uống (food court), rạp chiếu, khu vui chơi.
- Thường có tầng hầm đỗ xe (lấy mục hầm từ `drafting-sheet-set-highrise.md` khi có).
- Khoang cháy lớn, thoát nạn qua mall; mặt tiền có biển quảng cáo.

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 2 | Mặt bằng phân lô gian hàng mỗi tầng: mã gian, diện tích cho thuê (bảng), mặt tiền gian | 1:200 | CS | — | `thematic` |
| 2 | Mặt bằng giếng trời + thang cuốn (lỗ sàn, lan can) | 1:100 | KT | — | `enlarged` |
| 2 | Mặt bằng luồng hàng hoá: bến xuất nhập, hành lang dịch vụ, kho, rác | 1:200 | KT | — | `thematic` |
| 3 | Mặt đứng + vị trí biển quảng cáo | 1:200–1:100 | KT | — | `elevation` |
| 6 | Gian hàng điển hình (nền giao cho người thuê): ranh giới, mặt tiền, đấu nối | 1:50 | TC | — | `enlarged` |
| 6 | Khu ăn uống, rạp chiếu (mặt cắt độ dốc khán phòng, tầm nhìn) | 1:100–1:50 | KT | — | `enlarged` |
| 8 | Chi tiết lan can giếng trời, mái kính giếng trời, khe co giãn | 1:20–1:5 | TC | — | `detail` |

## 5. Layout đặc thù

- **Khổ giấy**: A1 (công trình vừa) hoặc A0 (bệnh viện, TTTM lớn).
- **Mặt bằng lớn**: chia vùng (Z1–Z5). Các khối tách rời (trường nhiều khối, bệnh viện nhiều khối) thì mỗi khối là một bộ con, có thứ tự nhóm như nhau và cùng vị trí trên sheet (LO4).
- **Phòng điển hình**: lưới ô (LO9), mỗi ô một loại phòng, kèm key plan tô các vị trí lặp lại của phòng.
- **Mặt bằng phân khu / phân luồng**: mỗi tầng một sheet tổng, legend màu ở cột phải (LO8), cùng màu trong cả bộ.

## 6. Hay sót (kiểm khi lập danh mục)

- Thiếu mặt bằng tiếp cận người khuyết tật, thiếu vệ sinh người khuyết tật.
- Thiếu mặt bằng phân luồng (bệnh viện), thiếu luồng hàng hoá (TTTM).
- Phòng điển hình không có key plan chỉ vị trí lặp lại.
- Trường học thiếu chi tiết lan can an toàn trẻ em.
- Bệnh viện thiếu mặt cắt lớp che chắn bức xạ.
- TTTM thiếu bảng diện tích cho thuê, hoặc bảng không khớp với mặt bằng.
