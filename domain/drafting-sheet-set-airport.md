---
name: drafting-sheet-set-airport
description: "Bộ hồ sơ kiến trúc sân bay (nhà ga hành khách, nhà ga hàng hoá, công trình phụ trợ): quy mô rất lớn, chia vùng bắt buộc, key plan, lưới trục lớn, phân luồng hành khách đi / đến / quốc tế / quốc nội, landside / airside, an ninh, hành lý, cửa ra tàu bay, mái lớn, mặt dựng, biển chỉ dẫn; tỷ lệ, cách xếp sheet, hay sót. Airport terminal sheet set."
metadata:
  updated: "2026-11-05"
  related: ["drafting-sheet-layout"]
---

# Bộ hồ sơ: sân bay (Airport terminal)

File này cộng thêm vào bộ hồ sơ lõi (`drafting-sheet-layout.md` §4). GĐ, nhóm và các mã LO / Z dùng như trong file đó.

## 1. Đặc điểm ảnh hưởng tới hồ sơ

- **Quy mô rất lớn**:
  - nhà ga dài hàng trăm mét tới hơn một km, mỗi tầng hàng vạn m²;
  - ở 1:100 không vừa bất kỳ khổ nào, nên **chia vùng là bắt buộc** (Z1–Z5);
  - mỗi tầng có một mặt bằng tổng 1:500 hoặc 1:1000.
- **Lưới trục lớn**:
  - nhịp lớn, có trục phụ, có khi dùng trục cong / trục toả tia;
  - tên trục dùng thống nhất với kết cấu.
- **Tầng theo luồng khách**: tầng đi, tầng đến, tầng trung gian / cầu nối, tầng hành lý (BHS), hầm, tầng kỹ thuật, mái lớn.
- **Phân khu an ninh**:
  - phía ngoài (landside) và phía trong (airside);
  - khu cách ly; quốc tế và quốc nội;
  - điểm soi chiếu an ninh, xuất nhập cảnh, hải quan, kiểm dịch.
- **Luồng khách và hành lý** là xương sống của thiết kế:
  - khách đi, khách đến, khách nối chuyến (transit), khách VIP;
  - nhân viên, hàng hoá, hành lý.
- **Cửa ra tàu bay**: phòng chờ, cầu ống lồng (PBB), cửa ra xe buýt sân đỗ. Cao độ sàn khớp với sân đỗ (apron) và kiểu tàu bay.
- **Kết cấu mái lớn**: kết cấu không gian / dàn thép, mái kim loại lớn, cửa lấy sáng, thoát nước mái (siphonic), mặt dựng kính lớn.
- **Phối hợp nhiều hệ** cần mặt bằng nền (background) cho các hệ: BHS, thang cuốn, băng chuyền người đi bộ, thang máy, biển chỉ dẫn (wayfinding), FIDS, quầy làm thủ tục.
- **Tham khảo quốc tế**:
  - IATA ADRM cho mức phục vụ (LOS) và diện tích khu chức năng;
  - ICAO Annex 14 cho phía sân đỗ / đường lăn.
  - Quy định của nhà chức trách hàng không và an ninh hàng không của dự án ưu tiên.

## 2. Bộ bản vẽ (cộng vào bộ lõi)

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 0 | Bảng chỉ tiêu: công suất (triệu khách/năm), giờ cao điểm, diện tích từng khu chức năng, số quầy / cửa ra / băng chuyền | — | CS | — | `schedule` |
| 0 | Sơ đồ chia vùng + key plan chuẩn của cả dự án | không tỷ lệ | CS | Tên vùng giữ nguyên toàn bộ hồ sơ (Z5) | `notes` |
| 1 | Mặt bằng tổng thể cảng: nhà ga, sân đỗ, đường lăn, đường giao thông phía ngoài, nhà xe, công trình phụ trợ | 1:5000–1:2000 | CS | — | `site` |
| 1 | Tổng mặt bằng nhà ga + phía ngoài (đường dẫn, cầu cạn đến / đi, bãi xe) | 1:1000–1:500 | CS | — | `site` |
| 2 | Mặt bằng tổng mỗi tầng (chỉ dẫn vùng, Z4) | 1:1000–1:500 | CS | — | `plan` |
| 2 | Mặt bằng mỗi tầng theo vùng | 1:200 (CS) · 1:100 (KT, TC) | CS | Matchline, key plan | `plan` |
| 2 | Mặt bằng phân luồng: đi, đến, nối chuyến, quốc tế / quốc nội, VIP, nhân viên (màu + legend) | 1:1000–1:500 | CS | — | `thematic` |
| 2 | Mặt bằng phân khu an ninh: landside / airside, cách ly, điểm soi chiếu, cửa kiểm soát | 1:1000–1:500 | CS | — | `thematic` |
| 2 | Mặt bằng hệ hành lý (nền phối hợp BHS): băng chuyền, phòng phân loại, băng trả hành lý | 1:500–1:200 | KT | — | `thematic` |
| 2 | Mặt bằng mái: module mái, cửa lấy sáng, thoát nước, đường đi bảo trì | 1:500 tổng + 1:200 theo vùng | CS | — | `roof` |
| 2 | Mặt bằng trần theo vùng (module trần, cao độ, cửa thăm) | 1:200–1:100 | TC | — | `ceiling` |
| 2 | Mặt bằng thoát nạn / PCCC mỗi tầng (khoang cháy, lối thoát, khoảng cách) | 1:500 | CS | — | `thematic` |
| 2 | Mặt bằng biển chỉ dẫn (wayfinding) | 1:500–1:200 | TC | — | `thematic` |
| 3 | Mặt đứng tổng mọi hướng (phía ngoài, phía sân đỗ) | 1:1000–1:500 | CS | — | `elevation` |
| 3 | Mặt đứng theo đoạn | 1:200 | KT | — | `elevation` |
| 3 | Triển khai mặt dựng kính lớn | 1:100–1:50 | TC | — | `facade` |
| 4 | Mặt cắt tổng ngang nhà ga (đường phía ngoài → sảnh → phòng chờ → cầu ống lồng → sân đỗ) | 1:500 | CS | Cao độ các tầng, đường, sân đỗ | `section` |
| 4 | Mặt cắt tổng dọc | 1:500 | CS | — | `section` |
| 4 | Mặt cắt chi tiết theo vùng / theo trục | 1:200–1:100 | KT | — | `section` |
| 5 | Thang cuốn, băng chuyền người đi bộ, thang máy, thang bộ thoát nạn: mặt bằng + mặt cắt | 1:100–1:50 | KT | — | `vertical`, `stair` |
| 6 | Khu làm thủ tục (quầy check-in, băng cân hành lý) | 1:100–1:50 | KT | — | `enlarged` |
| 6 | Khu soi chiếu an ninh, xuất nhập cảnh, hải quan | 1:100–1:50 | KT | — | `enlarged` |
| 6 | Phòng chờ cửa ra + cầu ống lồng / cửa ra xe buýt sân đỗ | 1:100–1:50 | KT | — | `enlarged` |
| 6 | Khu nhận hành lý | 1:100 | KT | — | `enlarged` |
| 6 | Phòng chờ hạng thương gia / VIP | 1:50 | TC | — | `enlarged` |
| 6 | Chi tiết khu vệ sinh công cộng điển hình (cả phòng người khuyết tật, phòng mẹ và bé) | 1:50–1:25 | TC | Hai mặt bằng: kích thước, lát sàn (CT1) | `enlarged` |
| 6 | Khu thương mại / ăn uống: mặt bằng phân lô gian hàng | 1:200–1:100 | KT | — | `thematic` |
| 7 | Cửa, vách kính, cửa an ninh / kiểm soát (thống kê kèm cấp an ninh, chống cháy) | 1:50 | KT | — | `door` |
| 8 | Chi tiết mái lớn: máng thu, cửa lấy sáng, diềm, chống thấm, khe co giãn | 1:20–1:5 | TC | — | `detail` |
| 8 | Chi tiết mặt dựng kính lớn: neo, chân, đỉnh, khe co giãn | 1:20–1:5 | TC | — | `detail` |
| 8 | Chi tiết khe co giãn kết cấu giữa các khối | 1:10–1:5 | TC | — | `detail` |

## 3. Layout đặc thù

- **Khổ giấy**: A0 hoặc A1, cả bộ một khổ.
- **Chia vùng là xương sống của layout**:
  - một sơ đồ vùng duy nhất cho mọi tầng, mọi loại mặt bằng (bố trí, trần, mái, hoàn thiện, thoát nạn);
  - mỗi sheet vùng có key plan tô vùng ở góc dưới phải (LO8, Z3);
  - matchline kèm số sheet kề bên (Z2).
- **Thứ tự sheet**: theo tầng rồi theo vùng (Z5), trừ khi dự án chọn theo vùng.
- **Mặt bằng phân luồng / an ninh**: mỗi chủ đề một sheet tổng mỗi tầng. Legend màu ở cột phải (LO8); cùng một màu cho cùng một luồng trong cả bộ.
- **Mặt cắt tổng ngang** rất dài: đặt riêng một sheet ngang. Mặt cắt chi tiết theo vùng đặt cùng vị trí trục (LO4).
- **Mặt bằng nền phối hợp** (BHS, wayfinding) dùng view template riêng (nét kiến trúc mờ), không trộn với mặt bằng kiến trúc chính.

## 4. Hay sót (kiểm khi lập danh mục)

- Sơ đồ vùng khác nhau giữa các loại mặt bằng hoặc giữa các tầng (vi phạm Z1).
- Sheet vùng thiếu key plan, thiếu matchline, hoặc matchline ghi sai số sheet.
- Thiếu mặt bằng phân luồng hoặc phân khu an ninh.
- Mặt cắt tổng không đi qua cầu ống lồng xuống tới sân đỗ.
- Thiếu cao độ sân đỗ và đường phía ngoài trên mặt cắt.
- Thiếu mặt bằng nền cho các hệ phối hợp (BHS, thang cuốn, biển chỉ dẫn).
- Thiếu khe co giãn giữa các khối, thiếu chi tiết mái lớn.
