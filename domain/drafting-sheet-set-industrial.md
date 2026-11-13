---
name: drafting-sheet-set-industrial
description: "Bộ hồ sơ kiến trúc công trình công nghiệp (nhà xưởng, kho, logistics): tổng mặt bằng nhà máy, xưởng khẩu độ lớn, mezzanine văn phòng, mái tôn + lấy sáng + thông gió, cửa cuốn, dock xuất nhập hàng, sàn công nghiệp, vách bao che; tỷ lệ, cách xếp sheet, hay sót. Industrial building sheet set."
metadata:
  updated: "2026-11-05"
  related: ["drafting-sheet-layout"]
---

# Bộ hồ sơ: công nghiệp (Factory, warehouse)

File này cộng thêm vào bộ hồ sơ lõi (`drafting-sheet-layout.md` §4). GĐ, nhóm và các mã LO / Z dùng như trong file đó.

## 1. Đặc điểm ảnh hưởng tới hồ sơ

- **Nhà máy là một tổ hợp nhiều hạng mục**: xưởng chính, kho, nhà văn phòng, nhà ăn, nhà xe, nhà bảo vệ, trạm điện, bể nước + trạm bơm PCCC, xử lý nước thải.
  - Tổng mặt bằng là bản vẽ chính.
  - Mỗi hạng mục là một bộ con.
- **Xưởng**:
  - một tầng, khẩu độ lớn, khung thép (thường tiền chế); có thể có mezzanine văn phòng / kỹ thuật bên trong;
  - lưới trục thưa, rất dài theo một phương.
- **Bao che**:
  - tường gạch / bê tông chân tường (thường cao 1,2–3 m), phía trên là tôn;
  - mái tôn dốc nhỏ, tấm lấy sáng, quạt / cửa thông gió mái, máng xối dài.
- **Vận hành**:
  - cửa cuốn lớn, dock xuất nhập hàng (dock leveler, mái che dock);
  - đường xe nâng, đường xe container;
  - sàn công nghiệp (khe co giãn, tải trọng, phủ chống bụi / epoxy).
- **Phân vùng**: phòng sạch, khu nguy hiểm cháy nổ, kho hoá chất. Theo hạng nguy hiểm cháy của quy chuẩn PCCC.

## 2. Bộ bản vẽ (cộng vào bộ lõi)

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc | Loại |
|---|---|---|---|---|---|
| 0 | Bảng chỉ tiêu: diện tích từng hạng mục, mật độ, cây xanh, đường bãi | — | CS | — | `schedule` |
| 1 | Tổng mặt bằng nhà máy: hạng mục, đường nội bộ, bán kính quay xe container, bãi xe, cổng, cây xanh, khoảng cách PCCC | 1:1000–1:500 | CS | Đường cho xe chữa cháy | `site` |
| 1 | Mặt bằng định vị các hạng mục + cao độ san nền | 1:500 | KT | — | `site` |
| 1 | Hạng mục phụ (nhà bảo vệ, nhà xe, trạm bơm, nhà rác): bộ nhỏ | 1:100–1:50 | KT | Mặt bằng, mặt đứng, mặt cắt | `subset` |
| 2 | Mặt bằng xưởng / kho | 1:200 (chia vùng nếu cần) | CS | Lưới trục, cửa cuốn, dock, đường xe nâng, khu chức năng | `plan` |
| 2 | Mặt bằng mezzanine / văn phòng trong xưởng | 1:100–1:50 | KT | — | `plan` |
| 2 | Mặt bằng sàn công nghiệp: khe co giãn / khe cắt, rãnh thu, cao độ, độ dốc | 1:200 | TC | — | `plan-finish` |
| 2 | Mặt bằng mái: độ dốc, tấm lấy sáng, quạt / cửa thông gió mái, máng xối, ống đứng | 1:200 | CS | — | `roof` |
| 3 | Mặt đứng 4 hướng | 1:200–1:100 | CS | Cao độ chân tường, đỉnh tôn, đỉnh mái, cửa cuốn | `elevation` |
| 4 | Mặt cắt ngang qua khung | 1:100 | CS | Khẩu độ, cao độ đỉnh cột, đáy kèo, độ dốc mái | `section` |
| 4 | Mặt cắt dọc | 1:200 | KT | — | `section` |
| 4 | Mặt cắt qua dock | 1:50 | KT | Cao độ sàn dock so với sân xe | `section` |
| 6 | Nhà văn phòng / nhà ăn (nếu tách khối): bộ con theo bộ lõi | 1:100 | KT | — | `subset` |
| 6 | Chi tiết vệ sinh, thay đồ công nhân | 1:50 | TC | Hai mặt bằng: kích thước, lát sàn (CT1) | `enlarged` |
| 7 | Thống kê cửa cuốn, cửa thoát nạn, cửa chống cháy | 1:50 | KT | — | `door` |
| 8 | Chi tiết vách bao che: chân tường, diềm tôn, góc, cửa sổ trên vách tôn | 1:20–1:5 | TC | — | `detail` |
| 8 | Chi tiết mái: máng xối, tấm lấy sáng, sống mái, quạt thông gió | 1:20–1:5 | TC | — | `detail` |
| 8 | Chi tiết dock: dock leveler, mái che, ụ chắn, gờ bảo vệ | 1:20–1:10 | TC | — | `detail` |
| 8 | Chi tiết khe co giãn sàn, gờ chống va chân cột / cửa | 1:10–1:5 | TC | — | `detail` |

## 3. Layout đặc thù

- **Khổ giấy**: A1 hoặc A0, sheet ngang.
- **Mặt bằng xưởng**:
  - xưởng rất dài: đặt theo chiều ngang của sheet, cùng hướng trong cả bộ;
  - không vừa 1:200 thì chia vùng theo khe nhiệt / khe lún (Z2).
- **Mặt cắt ngang khung** đặt trên mặt bằng hoặc cạnh mặt bằng, cùng tỷ lệ khi vừa, để trục thẳng hàng (LO5).
- **Mặt đứng dài**: 2 mặt đứng một sheet, xếp trên dưới, trục thẳng hàng (LO5).
- **Mỗi hạng mục là một bộ con**, trong số sheet có mã hạng mục (theo profile). Tổng mặt bằng và bảng chỉ tiêu ở nhóm 0, nhóm 1, dùng chung cho cả nhà máy.

## 4. Hay sót (kiểm khi lập danh mục)

- Thiếu bán kính quay xe container và đường xe chữa cháy trên tổng mặt bằng.
- Thiếu mặt bằng sàn công nghiệp (khe co giãn, rãnh thu, độ dốc).
- Mặt bằng mái thiếu tấm lấy sáng, thông gió mái, ống đứng.
- Thiếu mặt cắt qua dock; thiếu cao độ dock so với sân.
- Thiếu chi tiết chân tường / vách tôn, thiếu gờ chống va.
- Hạng mục phụ (trạm bơm, nhà bảo vệ) không có bộ con.
