---
name: drafting-sheet-set-townhouse
description: "Bộ hồ sơ kiến trúc nhà liên kế (nhà phố, nhà ống, liền kề dự án, shophouse): danh mục điển hình theo thứ tự sheet (user), bản vẽ cần có theo giai đoạn, tỷ lệ, cách xếp sheet cho lô hẹp dài, mẫu nhà / lô góc, hay sót. Townhouse sheet set."
metadata:
  updated: "2026-11-05"
  related: ["drafting-sheet-layout"]
---

# Bộ hồ sơ: nhà liên kế (Townhouse)

Danh mục ở §2 **thay cho** bộ hồ sơ lõi (`drafting-sheet-layout.md` §4). GĐ, nhóm và các mã LO / Z dùng như trong file đó.

## 1. Đặc điểm ảnh hưởng tới hồ sơ

- **Lô hẹp và dài**: mặt tiền thường 4–8 m, sâu 15–25 m; cao 3–7 tầng, có thể thêm tum thang, tầng lửng, một tầng hầm.
- **Hai bên giáp nhà bên** (tường chung hoặc tường sát ranh):
  - chỉ có mặt đứng trước và mặt đứng sau;
  - lô góc có thêm mặt đứng bên.
- **Ánh sáng lấy từ giếng trời** và thông tầng. Thang thường nằm giữa nhà, cạnh giếng trời.
- **Quy hoạch quản lý chặt**:
  - chỉ giới xây dựng, lộ giới, độ vươn ban công / ô văng;
  - chiều cao tầng 1 và cao độ chuẩn của dãy phố;
  - số tầng, tầng tum, mái.
- **Nhà liền kề dự án**:
  - nhiều căn cùng **mẫu nhà** (mẫu điển hình, mẫu lô góc, mẫu đối xứng);
  - hồ sơ theo bộ con của từng mẫu, kèm hồ sơ dãy nhà.
- **Shophouse**: tầng 1–2 kinh doanh, có lối lên ở riêng; mặt tiền có biển hiệu và mái đón.

## 2. Danh mục điển hình (user, 2026-11-05)

Đây là danh mục **thay cho** bộ lõi khi job là nhà liên kế. Thứ tự các dòng chính là thứ tự sheet trong bộ hồ sơ.

**Cách gọi tầng**: theo profile.
- Mặc định: hầm, trệt, lầu 1…n, tum.
- Dòng "lầu 1…n" khai triển thành một dòng cho mỗi lầu có thật trong model.

**Nhóm cửa đặt cuối bộ**, sau các chi tiết (thói quen của user). Cách này thay cho thứ tự nhóm mặc định trong `drafting-sheet-layout.md` §3.

**Cột Điều kiện**: dòng có điều kiện chỉ bắt buộc khi điều kiện đó đúng (`drafting-sheet-layout.md` §2). Các dòng ghi "bổ sung 2026-11-05" là phần Claude thêm, chờ user duyệt. Card 01 vẫn hỏi user các dòng này.

| # | Nhóm | Bản vẽ | Tỷ lệ | GĐ | Điều kiện | Nội dung bắt buộc |
|---|---|---|---|---|---|---|
| 1 | 0 | Tờ bìa | — | CS | — | Tên công trình, địa điểm, chủ đầu tư, đơn vị thiết kế, giai đoạn, ngày |
| 2 | 0 | Danh mục bản vẽ | — | CS | — | Khớp print set (`drafting-views-sheets.md` §5) |
| 3 | 0 | Ghi chú, ký hiệu chung | — | KT | — | Ký hiệu, chữ viết tắt, vật liệu, cao độ chuẩn ±0.000; bảng diện tích các tầng nếu không đặt trên mặt bằng tổng thể |
| 4 | 0 | Chi tiết cấu tạo sàn, trần, tường | 1:10–1:5 | TC | — | Một mã cho mỗi lớp cấu tạo; dùng lại mã đó trên mặt bằng và mặt cắt; có chống thấm sàn vệ sinh, sân thượng, tường hầm |
| 5 | 1 | Mặt bằng vị trí khu đất | 1:2000–1:500 | CS | — | Vị trí trong khu, đường xung quanh, hướng Bắc |
| 6 | 1 | Mặt bằng định vị khu đất | 1:200–1:100 | CS | — | Mốc toạ độ, kích thước tới ranh, chỉ giới xây dựng, lộ giới |
| 7 | 1 | Mặt bằng tổng thể | 1:200–1:100 | CS | — | Nhà, vỉa hè, lối vào xe, cao độ nền và vỉa hè; dự án: dãy nhà, mã mẫu nhà, lô góc |
| 8 | 2 | Mặt bằng sàn hầm | 1:100–1:50 | CS | có hầm | Ram dốc (cao độ đầu / cuối), rãnh thu, hố ga, hố pit thang máy, cao độ sàn |
| 9 | 2 | Mặt bằng sàn trệt | 1:100–1:50 | CS | — | Chỉ giới, lộ giới, cao độ vỉa hè, tam cấp, giếng trời |
| 10 | 2 | Mặt bằng sàn lầu 1…n (mỗi lầu một sheet) | 1:100–1:50 | CS | — | Ban công vươn ra (so với chỉ giới), lô gia, thông tầng, giếng trời |
| 11 | 2 | Mặt bằng sàn tum | 1:100–1:50 | CS | có tum | — |
| 12 | 2 | Mặt bằng mái, sân thượng | 1:100–1:50 | CS | — | Độ dốc, phễu thu, sê nô, ống đứng, mái tum, mái che giếng trời |
| 13 | 2 | Mặt bằng trần hầm | 1:100–1:50 | TC | có hầm và hầm có trần (bổ sung 2026-11-05) | — |
| 14 | 2 | Mặt bằng trần trệt | 1:100–1:50 | TC | — | Cao độ trần, vật liệu trần, giật cấp, đèn |
| 15 | 2 | Mặt bằng trần lầu 1…n | 1:100–1:50 | TC | — | Như trên |
| 16 | 2 | Mặt bằng trần tum | 1:100–1:50 | TC | tum có trần (bổ sung 2026-11-05) | — |
| 17 | 3 | Mặt đứng chính (mặt tiền) | 1:100–1:50 | CS | bổ sung 2026-11-05 | Cao độ tầng, chiều cao tầng trệt, chỉ giới, vật liệu, biển hiệu (shophouse) |
| 18 | 3 | Mặt đứng sau | 1:100–1:50 | CS | bổ sung 2026-11-05 | — |
| 19 | 3 | Mặt đứng bên | 1:100–1:50 | CS | lô góc, hoặc nhà cao hơn nhà bên (bổ sung 2026-11-05) | — |
| 20 | 4 | Mặt cắt dọc (qua thang và giếng trời) | 1:100–1:50 | CS | bổ sung 2026-11-05 | Cao độ tầng, chiều cao thông thuỷ, cao độ vỉa hè / nền / hầm |
| 21 | 4 | Mặt cắt ngang | 1:100–1:50 | KT | bổ sung 2026-11-05 | Tường chung với nhà bên, ban công |
| 22 | 5 | Chi tiết thang bộ 1…n (mỗi thang một sheet) | 1:50–1:20 | KT | — | Mặt bằng các tầng + mặt cắt của thang, lan can, tay vịn (`drafting-stair-plan`, `drafting-stair-section-*`) |
| 23 | 5 | Chi tiết thang máy | 1:50 | KT | có thang máy | Mặt bằng hố thang các tầng, mặt cắt (pit, OH), cửa tầng |
| 24 | 6 | Chi tiết nhà vệ sinh 1…n (mỗi loại WC một sheet) | 1:25–1:20 | TC | — | Mặt bằng phóng to, lát, triển khai tường, thiết bị, độ dốc thoát sàn, chống thấm |
| 25 | 8 | Chi tiết bậc tam cấp | 1:20–1:10 | TC | — | — |
| 26 | 8 | Chi tiết bồn hoa | 1:20–1:10 | TC | có bồn hoa | Lớp chống thấm, lỗ thoát nước |
| 27 | 8 | Chi tiết ramp dốc | 1:50–1:20 | TC | có hầm / lối xe | Độ dốc, rãnh thu đầu / chân ram, chống trượt |
| 28 | 8 | Chi tiết thu nước mưa | 1:20–1:5 | TC | — | Phễu thu, sê nô, ống đứng, cổ ống xuyên sàn |
| 29 | 8 | Chi tiết ban công, lô gia | 1:20–1:10 | TC | — | Lan can (chiều cao, khe hở), độ dốc thoát nước, gờ chắn |
| 30 | 8 | Chi tiết mái | 1:20–1:5 | TC | — | Chống thấm, diềm mái, mái tum, mái che giếng trời |
| 31 | 8 | Chi tiết mặt tiền | 1:20–1:10 | TC | mặt tiền có lam / phào / mái đón / biển hiệu (bổ sung 2026-11-05) | — |
| 32 | 8 | Chi tiết giếng trời | 1:20–1:10 | TC | có giếng trời (bổ sung 2026-11-05) | Mái kính / mái che, lan can, thoát nước |
| 33 | 8 | Chi tiết tiếp giáp nhà bên, khe lún | 1:20–1:10 | TC | dự án nhiều căn / có nhà bên (bổ sung 2026-11-05) | Chống thấm tường chung, diềm, khe lún |
| 34 | 7 | Bảng thống kê cửa đi, cửa sổ | — | KT | — | Mã cửa, kích thước, số lượng, vật liệu, vị trí (tầng) |
| 35 | 7 | Chi tiết cửa đi, cửa sổ | 1:50–1:20 | TC | — | Mặt đứng từng mã cửa, chiều mở, phụ kiện; cửa cuốn / cửa xếp mặt tiền |

**Hỏi user khi lập danh mục** (card 01). Các bản vẽ sau tuỳ hợp đồng và thói quen văn phòng:
- mặt bằng kích thước tách riêng khỏi mặt bằng sàn;
- mặt bằng lát sàn (hoàn thiện sàn);
- mặt bằng xây tường;
- mặt bằng bố trí nội thất;
- chi tiết bếp;
- chi tiết lan can, tay vịn;
- chi tiết cổng (khi có sân trước).

### Bổ sung cho nhà liền kề dự án (nhiều căn, nhiều mẫu)

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Nội dung bắt buộc |
|---|---|---|---|---|
| 1 | Mặt bằng tổ hợp dãy nhà | 1:200 | KT | Các căn ghép thành dãy, tường chung, khe lún giữa các căn |
| 1 | Bảng phân loại căn theo mẫu | — | CS | Mã lô → mẫu nhà, diện tích, số tầng, đối xứng / không |
| 3 | Mặt đứng dãy phố | 1:200 | CS | Nhiều căn liền nhau, cao độ chuẩn của dãy |

## 3. Layout đặc thù

- **Khổ giấy**: nhà đơn lẻ dùng A3 (đôi khi A2). Dự án dùng A1, với nhiều mẫu nhà trên cùng một khổ.
- **Mặt bằng lô hẹp**:
  - đặt 2–4 mặt bằng tầng cạnh nhau trên một sheet ngang, cùng tỷ lệ, cùng hướng;
  - trục chữ (A, B…) nằm cùng một hàng ngang (LO5);
  - thứ tự từ trái sang phải là tầng thấp → cao (LO3);
  - không xoay mặt bằng khác hướng với cả bộ.
- **Mặt đứng trước + mặt cắt dọc** nên chung một sheet, cao độ thẳng hàng (LO5).
- **Dự án nhiều mẫu nhà**: mỗi mẫu nhà là một bộ con có cùng thứ tự nhóm và cùng vị trí view trên sheet (LO4). Mẫu đối xứng (mirror): ghi "ĐỐI XỨNG MẪU …" trên title, không vẽ lại khi chủ đầu tư không yêu cầu.

## 4. Hay sót (kiểm khi lập danh mục)

- Chỉ giới xây dựng, lộ giới và độ vươn ban công chưa ghi trên mặt bằng tầng 1 và mặt đứng trước.
- Thiếu mặt đứng và mặt cắt: đây là phần bắt buộc từ giai đoạn CS.
- Thiếu mặt bằng tum, mặt bằng trần tum, mặt bằng mái che giếng trời.
- Số sheet mặt bằng sàn / trần không khớp với số tầng thật của model (thiếu một lầu, hoặc thừa tầng tum).
- Lô góc vẫn dùng bộ của mẫu giữa dãy, thiếu mặt đứng bên.
- Thiếu chi tiết tiếp giáp với nhà bên: chống thấm tường chung, khe lún, diềm mái.
- Shophouse thiếu triển khai mặt tiền tầng 1 (cửa xếp / cửa cuốn, biển hiệu, mái đón).
