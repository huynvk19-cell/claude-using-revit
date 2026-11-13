---
name: drafting-sheet-layout
description: "Chuẩn layout bộ hồ sơ kiến trúc trên sheet: loại công trình → bộ hồ sơ, giai đoạn hồ sơ, nhóm và đánh số bản vẽ, bộ hồ sơ lõi, thành phần bắt buộc của bản vẽ chi tiết vệ sinh / thang bộ / thang máy (CT1–CT3), khổ giấy, tỷ lệ, bố cục viewport trên sheet (LO1–LO12), chia vùng + key plan + matchline (Z1–Z5), tên gọi đầy đủ của sheet và view (TG1–TG5), danh mục. Sheet set and sheet layout standard."
metadata:
  updated: "2026-11-13"
  related: ["drafting-sheet-create", "drafting-view-create", "drafting-sheet-place", "drafting-views-sheets", "drafting-annotation", "drafting-work-rules", "drafting-sheet-set-townhouse", "drafting-sheet-set-villa", "drafting-sheet-set-highrise", "drafting-sheet-set-airport", "drafting-sheet-set-public", "drafting-sheet-set-industrial"]
---

# Layout bộ hồ sơ và sheet (Sheet set and sheet layout)

Áp dụng cho bộ hồ sơ **kiến trúc** đặt trên sheet Revit. Hồ sơ kết cấu, MEP, PCCC chỉ được nhắc tới khi kiến trúc phải phối hợp.

Skill: `drafting-sheet-layout`. Mỗi thẻ bước của skill dẫn về một mục dưới đây.

## 1. Loại công trình → bộ hồ sơ (HS1)

Mỗi loại công trình cần một bộ bản vẽ khác nhau. Lấy loại công trình từ `drafting-profile.md`. Sau đó chỉ mở **đúng một** file bộ hồ sơ của loại đó; không mở file của loại khác.

| Loại công trình | Gồm | File bộ hồ sơ |
|---|---|---|
| Nhà liên kế | nhà phố, nhà ống, nhà liền kề dự án, shophouse | `drafting-sheet-set-townhouse.md` |
| Biệt thự | đơn lập, song lập, nhà vườn, biệt thự nghỉ dưỡng | `drafting-sheet-set-villa.md` |
| Nhà cao tầng | chung cư, văn phòng, khách sạn, hỗn hợp (khối đế + tháp), có tầng hầm | `drafting-sheet-set-highrise.md` |
| Sân bay | nhà ga hành khách, nhà ga hàng hoá, công trình phụ trợ của cảng | `drafting-sheet-set-airport.md` |
| Công trình công cộng | trường học, bệnh viện, trung tâm thương mại, văn hoá, thể thao | `drafting-sheet-set-public.md` |
| Công nghiệp | nhà xưởng, kho, trung tâm logistics | `drafting-sheet-set-industrial.md` |

- **Công trình hỗn hợp**: lấy bộ hồ sơ của phần chính, rồi thêm các bản vẽ đặc thù của phần kia (vd. chung cư có khối đế thương mại: dùng bộ nhà cao tầng, thêm mục gian hàng của TTTM). Ghi rõ sự kết hợp vào profile.
- **Loại chưa có trong bảng**: dùng bộ hồ sơ lõi (§4) và hỏi user các bản vẽ đặc thù. Khi user đã duyệt, ghi lại thành một file bộ hồ sơ mới.

## 2. Giai đoạn hồ sơ (HS2)

| Mã | Giai đoạn | Độ sâu |
|---|---|---|
| YT | Ý tưởng / phương án | Mặt bằng, mặt đứng, mặt cắt chính ở tỷ lệ nhỏ; có phối cảnh; chưa có chi tiết |
| CS | Thiết kế cơ sở | Đủ tổng mặt bằng, mặt bằng các tầng, mặt đứng, mặt cắt chính (1:200–1:100); kích thước tổng và trục; có chỉ tiêu |
| KT | Thiết kế kỹ thuật | Đủ mặt bằng, mặt đứng, mặt cắt (1:100); kích thước chi tiết; chi tiết cấu tạo chính; vật liệu |
| TC | Thiết kế bản vẽ thi công | Đủ để thi công: khai triển mọi mặt bằng (kích thước, hoàn thiện, trần), chi tiết 1:50–1:5, thống kê cửa và hoàn thiện |
| HC | Hoàn công | Như TC, cộng với ghi chú các thay đổi so với hồ sơ đã duyệt |

Trong các bảng bộ hồ sơ, cột **GĐ** ghi giai đoạn sớm nhất mà bản vẽ đó phải có. Một bản vẽ là **bắt buộc** khi GĐ của nó không muộn hơn giai đoạn của job, theo thứ tự YT < CS < KT < TC < HC. Bản vẽ có ghi điều kiện (vd. "nếu có tầng hầm") chỉ bắt buộc khi điều kiện đó đúng.

## 3. Nhóm và đánh số bản vẽ (HS3)

Bộ hồ sơ được xếp theo thứ tự đọc: từ cái chung đến cái riêng, từ tổng thể đến chi tiết.

| Nhóm | Nội dung |
|---|---|
| 0 Chung | Bìa, danh mục bản vẽ, ghi chú chung, ký hiệu và chữ viết tắt, bảng vật liệu / hoàn thiện, bảng chỉ tiêu |
| 1 Tổng thể | Vị trí, tổng mặt bằng, mặt bằng định vị, mặt bằng sân vườn / hoàn thiện ngoài nhà, cổng, tường rào |
| 2 Mặt bằng | Đi theo từng loại mặt bằng (bố trí, kích thước, hoàn thiện sàn, trần, mái); trong mỗi loại, theo tầng từ thấp lên cao: hầm → tầng 1 → … → mái |
| 3 Mặt đứng | Mặt đứng tổng, rồi mặt đứng chi tiết / triển khai mặt dựng |
| 4 Mặt cắt | Mặt cắt tổng, rồi mặt cắt chi tiết theo trục |
| 5 Thang và lõi | Thang bộ, thang máy, thang cuốn, ram dốc |
| 6 Khu đặc thù | Chi tiết vệ sinh, bếp, phòng điển hình (căn hộ, phòng học, phòng bệnh…), mặt bằng phóng to và triển khai tường |
| 7 Cửa và vách | Thống kê cửa, chi tiết cửa, vách kính, mặt dựng |
| 8 Chi tiết cấu tạo | Tường, sàn, mái, chống thấm, lan can, ban công, ô văng, phào chỉ, khe lún |
| 9 Bảng thống kê | Hoàn thiện, cửa, thiết bị vệ sinh (khi tách khỏi các nhóm trên) |

- **Số sheet** = tiền tố số sheet của profile + số nhóm + số thứ tự trong nhóm. Định dạng (số chữ số, dấu nối) theo profile.
- Mỗi nhóm có một dải số riêng, có chừa chỗ trống, để chèn sheet sau này mà không phải đánh lại số.
- **Không đổi số của sheet đã phát hành.** Sheet thêm sau lấy số trống trong dải của nhóm, hoặc số kèm hậu tố theo profile.
- Hồ sơ của một dự án có nhiều mẫu nhà (nhiều mẫu nhà liên kế, biệt thự): mỗi mẫu là **một bộ con** với mã mẫu trong số sheet (theo profile). Các nhóm 0 và 1 dùng chung cho cả dự án.

## 4. Bộ hồ sơ lõi: mọi công trình đều có (HS4)

| Nhóm | Bản vẽ | Tỷ lệ | GĐ | Loại |
|---|---|---|---|---|
| 0 | Bìa (sheet riêng) | — | CS | `cover` |
| 0 | Danh mục bản vẽ (sheet riêng, §8) | — | CS | `sheet-list` |
| 0 | Ghi chú chung, ký hiệu, chữ viết tắt | — | KT | `notes` |
| 0 | Bảng vật liệu / hoàn thiện chung | — | KT | `schedule` |
| 1 | Tổng mặt bằng: chỉ giới, lộ giới, khoảng lùi, cao độ nền, hướng Bắc | 1:500–1:200 | CS | `site` |
| 1 | Mặt bằng định vị (toạ độ, kích thước tới ranh) | 1:200–1:100 | KT | `site` |
| 2 | Mặt bằng mỗi tầng: trục, kích thước tổng, tên + diện tích phòng, cao độ sàn | 1:100 | CS | `plan` |
| 2 | Mặt bằng mái: độ dốc, thoát nước, sê nô, ống đứng | 1:100 | CS | `roof` |
| 2 | Mặt bằng kích thước (khai triển) mỗi tầng | 1:100–1:50 | TC | `plan-dims` |
| 2 | Mặt bằng hoàn thiện sàn mỗi tầng | 1:100–1:50 | TC | `plan-finish` |
| 2 | Mặt bằng trần mỗi tầng | 1:100–1:50 | TC | `ceiling` |
| 3 | Mặt đứng: mọi hướng nhìn thấy được (hướng nào giáp nhà bên thì xem file của loại) | 1:100 | CS | `elevation` |
| 4 | Mặt cắt: ít nhất 1 dọc + 1 ngang, có ít nhất một mặt cắt qua thang | 1:100 | CS | `section` |
| 5 | Chi tiết thang bộ (mỗi thang): mặt bằng các tầng + mặt cắt + chi tiết phóng lớn mặt cắt thang + chi tiết mũi bậc (CT2) | 1:50–1:25; chi tiết 1:20–1:2 | KT | `stair` |
| 5 | Chi tiết thang máy, khi công trình có thang máy: mặt bằng hố thang + mặt cắt hố (CT3) | 1:50–1:25 | KT | `vertical` |
| 6 | Chi tiết vệ sinh (mỗi loại): mặt bằng kích thước + mặt bằng lát sàn + triển khai tường (CT1) | 1:50–1:25 | TC | `enlarged` |
| 7 | Thống kê cửa + mặt đứng cửa | 1:50 | KT | `door` |
| 8 | Chi tiết cấu tạo: tường, sàn, mái, chống thấm, lan can | 1:20–1:5 | TC | `detail` |

Cột `Loại` (ở bảng này và ở mọi file bộ hồ sơ) là khoá loại bản vẽ trong `drafting-coverage.md` §2: skill, lệnh và mức Claude làm được. Dòng mới luôn có `Loại`.

File bộ hồ sơ của từng loại công trình **cộng thêm hoặc sửa** bảng này; không chép lại các dòng của bảng lõi. Ngoại lệ: file có mục **Danh mục điển hình** (danh mục user đã duyệt) thì danh mục đó **thay hẳn** bảng lõi, và thứ tự các dòng của nó là thứ tự sheet.

## 4b. Thành phần bắt buộc của bản vẽ chi tiết (CT1–CT3, user 2026-11-13)

Áp dụng cho **mọi loại công trình**. Một dòng chi tiết của sổ chỉ đủ khi có mọi view con dưới đây; mỗi view con là một dòng con `ID.a`, `ID.b`… (§11). Tên view con theo TG3 (§8).

### CT1. Chi tiết vệ sinh: hai mặt bằng

Mỗi loại vệ sinh có **hai mặt bằng riêng**, không gộp nội dung vào một view:

| View con | Nội dung |
|---|---|
| `… - MẶT BẰNG KÍCH THƯỚC` | Kích thước thông thuỷ phòng, định vị tường / vách ngăn, cửa, tim thiết bị tới mặt tường hoàn thiện, ký hiệu triển khai tường |
| `… - MẶT BẰNG LÁT SÀN` | Vật liệu / mã lát, kích thước viên, điểm bắt đầu và hướng lát, cao độ hoàn thiện, độ dốc và hướng dốc về phễu thu, vị trí phễu thu, chênh cốt tại cửa |
| `… - TRIỂN KHAI TƯỜNG <tên>` | Mỗi tường có thiết bị hoặc ốp lát một view |

- Hai mặt bằng cùng ranh callout, cùng crop, cùng tỷ lệ.
- Trên sheet: hai mặt bằng **cùng hàng ngang**, kích thước bên trái, lát sàn bên phải, chung mép trên, cùng một trục gốc thẳng hàng (LO5); triển khai tường ở hàng dưới, đáy tường chung một đường ngang.
- Không vừa một sheet → tách `… - MẶT BẰNG` (hai mặt bằng) và `… - TRIỂN KHAI TƯỜNG`; hai mặt bằng luôn ở cùng sheet.

### CT2. Chi tiết thang bộ: chi tiết phóng lớn mặt cắt và mũi bậc

Mỗi thang bộ có đủ bốn phần:

| View con | Tỷ lệ | Nội dung |
|---|---|---|
| `… - MẶT BẰNG <tầng>` | 1:50–1:25 | Mỗi tầng thang phục vụ một view (`drafting-stair-plan`) |
| `… - MẶT CẮT <tên>` | 1:50–1:25 | Dọc vế; ngang vế khi cần (`drafting-stair-section-*`) |
| `… - CHI TIẾT PHÓNG LỚN MẶT CẮT <n>` | 1:20–1:10 | Phóng lớn từ mặt cắt thang, ít nhất tại: chân thang (bậc đầu gặp sàn), vế gặp chiếu nghỉ, bậc cuối gặp sàn tầng trên. Cấu tạo bậc (bản thang, bậc, lớp ốp), lan can và liên kết chân trụ, tay vịn |
| `… - CHI TIẾT MŨI BẬC` | 1:5–1:2 | Mũi bậc, mặt bậc, cổ bậc: vật liệu và chiều dày ốp, nẹp / rãnh chống trượt, len chân tường. Mỗi loại hoàn thiện bậc một chi tiết |

- Ký hiệu callout của chi tiết phóng lớn nằm trên mặt cắt thang; ký hiệu của mũi bậc nằm trên chi tiết phóng lớn (hoặc trên mặt cắt thang).
- Các thang cùng cấu tạo dùng chung một chi tiết mũi bậc: view chỉ đặt một lần (LO11), thang còn lại ghi tham chiếu.
- Trên sheet: mặt bằng xếp theo tầng thấp → cao; các chi tiết phóng lớn và mũi bậc xếp lưới ô (LO9), căn đỉnh theo hàng.
- Nội dung cấu tạo trong chi tiết phóng lớn và mũi bậc là mức 1 (`drafting-coverage.md`): Claude lập view, sheet, title; hình cấu tạo lấy từ thư viện chi tiết của văn phòng.

### CT3. Chi tiết thang máy: có thang máy thì có bản vẽ

Công trình **có thang máy** (kể cả thang máy gia đình của nhà liên kế, biệt thự) thì bộ hồ sơ có `CHI TIẾT THANG MÁY <n>`:

| View con | Nội dung |
|---|---|
| `… - MẶT BẰNG HỐ THANG <tầng>` | Mỗi tầng khác nhau một view (đáy pit, tầng điển hình, tầng trên cùng / phòng máy nếu có): kích thước thông thuỷ hố, cửa tầng (rộng thông thuỷ), định vị tới trục |
| `… - MẶT CẮT HỐ THANG` | Từ đáy pit tới nóc OH: chiều sâu pit, cao độ dừng tầng, chiều cao cửa tầng, chiều cao OH, móc treo / dầm đỡ nếu có |

- Nhận biết từ model khi lập sổ: family thang máy, shaft opening hoặc room tên theo profile (mặc định chứa `THANG MÁY`, `LIFT`, `ELEVATOR`). Không có → dòng `n/a`, lý do "không có thang máy".
- Thang máy nằm trong lõi thang bộ vẫn có dòng riêng; được đặt chung sheet với thang bộ khi vừa (nhà cao tầng: theo file của loại).

## 5. Khổ giấy và tỷ lệ (HS5)

**Khổ giấy**
- Cả bộ dùng **một khổ** và một khung tên (theo profile). Ngoại lệ (vd. A3 cho phụ lục) phải được user duyệt và ghi vào profile.
- Khổ ISO: A0 841×1189, A1 594×841, A2 420×594, A3 297×420 (mm).
- Tham khảo khi chọn khổ:
  - nhà ở nhỏ: A3, hoặc A2;
  - nhà liên kế, biệt thự (nhà ở thấp tầng): A2 (user, 2026-11-11);
  - nhà ở dự án nhiều tầng, công trình vừa: A1;
  - công trình lớn (nhà cao tầng, sân bay, TTTM): A1 hoặc A0.

**Tỷ lệ**
- Chỉ dùng dãy tỷ lệ chuẩn: 1:1, 1:2, 1:5, 1:10, 1:20, 1:25, 1:50, 1:100, 1:200, 1:500, 1:1000, 1:2000, 1:5000.
- Tỷ lệ lẻ (1:75, 1:150, 1:300…) chỉ được dùng khi profile cho phép.

| Loại view | Tỷ lệ thường dùng |
|---|---|
| Vị trí, tổng thể khu | 1:5000–1:1000 |
| Tổng mặt bằng | 1:500–1:200 |
| Mặt bằng tầng, mặt đứng, mặt cắt | 1:100; nhà ở nhỏ 1:50; công trình rất lớn 1:200 cho bản tổng, rồi 1:100 theo vùng |
| Thang, lõi, vệ sinh, bếp, phòng điển hình | 1:50–1:25 |
| Chi tiết cấu tạo | 1:20, 1:10, 1:5; mặt cắt profile 1:2–1:1 |
| Key plan | 1:1000–1:500, hoặc không ghi tỷ lệ (ghi "KHÔNG TỶ LỆ") |

**Kiểm vừa khổ**
- Kích thước trên giấy (mm) = kích thước vùng view thể hiện (crop + phần chú thích tràn ra, tính bằng m) × 1000 / mẫu số tỷ lệ.
- Kết quả phải ≤ vùng vẽ (LO1) trừ lề.
- Không vừa → làm theo thứ tự:
  1. tỷ lệ nhỏ hơn kế tiếp, nếu bảng tỷ lệ trên vẫn cho phép loại view đó;
  2. nếu không, chia vùng (§7);
  3. đổi khổ giấy chỉ khi cả bộ cùng đổi.

## 6. Bố cục trên sheet (LO1–LO12)

| Mã | Quy tắc |
|---|---|
| LO1 | **Vùng vẽ** = trong khung, trừ dải khung tên và cột ghi chú (nếu có), trừ lề tới khung. Lề và kích thước dải khung tên lấy theo profile; mặc định lề ≥ 10 mm giấy. **Khổ A2: viền khung cách mép giấy 5 mm** (user, 2026-11-11), khung tên A2 sửa theo; mọi chữ của khung tên (kể cả ngày in) nằm trong khung, không ra lề. Không viewport, title, legend nào được ra ngoài vùng vẽ. |
| LO2 | **Một sheet một chủ đề, một tỷ lệ chính.** View phụ (key plan, chi tiết nhỏ) được có tỷ lệ khác, nhưng tỷ lệ phải ghi trên title của view đó. |
| LO3 | **Thứ tự đọc**: trái → phải, trên → dưới. View chính (lớn nhất) đặt ở trên–trái hoặc chiếm giữa sheet. Mặt đứng xếp theo thứ tự trục (1→n, rồi A→n). Mặt cắt xếp theo số hiệu. Chi tiết xếp theo số. |
| LO4 | **Cùng vị trí giữa các sheet**: mặt bằng các tầng cùng loại (bố trí, kích thước, hoàn thiện, trần) đặt trên các sheet sao cho cùng một giao trục gốc nằm đúng cùng một điểm trên giấy (lật trang thấy trục chồng khít). Mặt bằng các vùng (§7) cũng vậy, theo từng vùng. |
| LO5 | **Thẳng hàng trên cùng sheet**: mặt đứng / mặt cắt xếp cạnh nhau thì cao độ ±0.000 và các level nằm trên cùng đường ngang; xếp trên dưới thì các trục cùng tên thẳng đứng một hàng. Các viewport cùng hàng căn chung mép trên hoặc mép dưới. |
| LO6 | **Khoảng hở**: giữa hai viewport, tính cả dim, tag, bubble trục và title, chừa ≥ 15 mm giấy (theo profile). Không viewport nào đè lên khung tên, ghi chú, legend hay viewport khác. |
| LO7 | **Title**: mỗi view một title, đặt dưới view, cùng kiểu cho cả bộ (title theo `drafting-annotation.md`, đặt bằng `drafting-tags-titles` 01). Số hiệu view đánh theo thứ tự đọc (LO3). |
| LO8 | **Ghi chú, legend, chỉ dẫn**: đặt ở cột phải, sát dải khung tên. Key plan ở góc dưới phải, ngay trên khung tên. Mọi mặt bằng và tổng mặt bằng có mũi tên hướng Bắc, cùng một góc trên mọi sheet. |
| LO9 | **Sheet chi tiết**: chia vùng vẽ thành lưới ô đều (vd. A1 thường 3×2 hoặc 4×3 ô); mỗi ô một chi tiết, căn đỉnh theo hàng. Các chi tiết cùng chủ đề nằm cùng một sheet. |
| LO10 | **Mật độ**: view chiếm khoảng 40–85 % vùng vẽ. Sheet quá trống → gộp với các view nhỏ cùng chủ đề. Sheet quá chật → tách sang sheet tiếp theo của cùng nhóm. |
| LO11 | **Mỗi view chỉ nằm trên một sheet.** Cùng một vùng cần hiện ở nhiều nơi → dùng dependent view hoặc duplicate; không đặt hai lần. Legend thì được đặt trên nhiều sheet. |
| LO12 | **Tham chiếu**: sau khi đặt hoặc dời view, các ký hiệu mặt cắt, callout, mặt đứng phải trỏ đúng số sheet / số view (Revit tự cập nhật). Ký hiệu nào còn trỏ tới view chưa đặt lên sheet → đưa vào việc tồn. |
| LO13 | **Không kẻ đường chia vùng giữa các view** (user, 2026-11-11: các đường kẻ không có nhiều ý nghĩa). Các view tách nhau bằng khoảng hở (LO6), mỗi view một ô bố cục; không thêm detail line trên sheet. |

## 7. Chia vùng công trình lớn (Z1–Z5)

| Mã | Quy tắc |
|---|---|
| Z1 | Chia vùng khi mặt bằng tầng ở tỷ lệ chuẩn không vừa vùng vẽ (§5). Chia **ít vùng nhất** có thể. Các vùng là scope box dùng chung; mỗi vùng là một dependent view. **Cùng ranh giới vùng cho mọi tầng và mọi loại mặt bằng.** |
| Z2 | Ranh giới vùng chạy theo trục (tại một trục, hoặc ở giữa nhịp) hoặc tại khe lún; tránh cắt qua phòng chính. Mỗi bên ranh giới có **matchline** kèm ghi chú "XEM BẢN VẼ <số sheet>". Hai vùng kề nhau chồng lên nhau ít nhất một nhịp trục. |
| Z3 | Mọi sheet vùng có **key plan** tô đậm vùng đang xem. Key plan dùng chung một legend view hoặc drafting view cho cả bộ. |
| Z4 | Mỗi tầng có một **mặt bằng tổng** ở tỷ lệ nhỏ (1:200–1:500), ghi tên các vùng, làm bản chỉ dẫn vùng. |
| Z5 | Thứ tự sheet: theo tầng rồi theo vùng (tầng 1 vùng A, B, C; rồi tầng 2…), trừ khi profile chọn theo vùng rồi theo tầng. Tên vùng giữ nguyên trong cả bộ. |

## 8. Tên sheet và danh mục (HS6)

- **Tên sheet** = loại bản vẽ + tầng / vùng / mẫu nhà, vd. `MẶT BẰNG KÍCH THƯỚC TẦNG 2 - VÙNG A`. Không ghi tỷ lệ trong tên.
- **Tên gọi đầy đủ cho sheet và view** (TG1–TG5, user 2026-11-13), mọi loại công trình:

| Mã | Quy tắc |
|---|---|
| TG1 | **Viết đủ chữ**: không viết tắt loại bản vẽ và đối tượng (`MB`, `MĐ`, `MC`, `CT`, `TK`, `WC`, `VS`…). Chỉ giữ mã có trong model: số / mã phòng, mã cửa, tên trục, tên tầng theo profile. |
| TG2 | **Tên sheet** = loại bản vẽ + đối tượng + vị trí (tầng / số / vùng / mẫu), thêm ` - <nội dung>` khi một đối tượng tách nhiều sheet. Sheet chi tiết bắt đầu bằng `CHI TIẾT`: `CHI TIẾT VỆ SINH 1`, `CHI TIẾT THANG BỘ 1 - MẶT CẮT`, `CHI TIẾT THANG MÁY 1`, `CHI TIẾT BẾP`. |
| TG3 | **Tên view** = tên bản vẽ của sổ; sheet có nhiều view thì thêm ` - <nội dung view>`: `CHI TIẾT VỆ SINH 1 - MẶT BẰNG KÍCH THƯỚC`, `CHI TIẾT VỆ SINH 1 - MẶT BẰNG LÁT SÀN`, `CHI TIẾT VỆ SINH 1 - TRIỂN KHAI TƯỜNG A`, `CHI TIẾT THANG BỘ 1 - MẶT BẰNG TẦNG 2`, `CHI TIẾT THANG BỘ 1 - CHI TIẾT MŨI BẬC`. Sheet một view: tên view = tên sheet. |
| TG4 | **Đọc riêng tên vẫn đủ nghĩa**: bản vẽ gì, của đối tượng nào, ở đâu. Không đặt tên trống đối tượng (`MẶT BẰNG`, `MẶT CẮT 1`, `TRIỂN KHAI BẮC`). |
| TG5 | **Tên dài không phải lý do viết tắt**: ô tên của khung tên cho xuống dòng hoặc thu chữ theo khung tên. Sheet đã phát hành chỉ đổi tên khi user đồng ý (HS3). |
- Cả bộ dùng cùng ngôn ngữ, cùng kiểu chữ (in hoa cho mọi chữ của hồ sơ, `drafting-annotation.md` §0), cùng cách gọi tầng. Song ngữ chỉ khi profile yêu cầu.
- Tầng điển hình (một view dùng cho nhiều tầng) ghi dải tầng: `MẶT BẰNG TẦNG 5–20 (ĐIỂN HÌNH)`.
- **Danh mục bản vẽ** liệt kê mọi sheet phát hành, theo thứ tự nhóm (§3). Print set phải khớp với danh mục (`drafting-views-sheets.md` §5).
- **Bìa và danh mục là hai sheet riêng** (user, 2026-11-11): bìa chỉ có thông tin công trình; danh mục ở sheet kế tiếp.
- **Cột danh mục, đúng thứ tự** (user, 2026-11-11): `STT | MÃ BẢN VẼ | TÊN BẢN VẼ | TỈ LỆ BẢN VẼ | PHIÊN BẢN | NGÀY PHIÊN BẢN`.
  - STT = tham số sheet `No.` (biến mới do nút **Chuẩn cột danh mục** / task `sheet_list_columns` tạo); `print_set_sync` đánh lại theo thứ tự bảng, không sửa tay.
  - MÃ / TÊN = Sheet Number / Sheet Name. PHIÊN BẢN / NGÀY PHIÊN BẢN = Current Revision / Current Revision Date.
  - TỈ LỆ = tham số sheet `SCALE` (text): các tỷ lệ của view trên sheet, lớn trước nhỏ sau, vd. `1:25, 1:10`; sheet chỉ có bảng / ghi chú ghi `-`. Cập nhật lại sau mỗi lần đổi view trên sheet.
  - Tiêu đề cột ghi bằng chữ in hoa như trên; tiêu đề bảng `DANH MỤC BẢN VẼ`. Cột ký (Checked / Approved / Designed / Drawn By) ẩn.
- **Nhóm theo bộ bản vẽ** (user, 2026-11-11): bảng nhóm theo Sheet Collection (Revit 2025 trở lên); Revit 2023–2024 dùng tham số sheet `NHÓM BẢN VẼ` (text), giá trị = số nhóm §3 + tên, vd. `2 - MẶT BẰNG`. Mỗi nhóm có dòng tiêu đề, trong nhóm sắp theo số sheet.
- **Danh mục dài hơn vùng vẽ**: chia bảng thành cột trên cùng sheet, chỗ cắt nằm giữa hai nhóm (không cắt đôi một nhóm), các cột cùng mép trên.

## 8b. Bảng ghi chú, ký hiệu, vật liệu chung của công trình nhỏ (KH1–KH8, user 2026-11-11, 2026-11-12)

Áp dụng cho hồ sơ quy mô nhỏ: **nhà liên kế, biệt thự**. Ghi chú, mọi ký hiệu và **bảng vật liệu / hoàn thiện chung** gom về **một bảng** trên sheet "Ghi chú, ký hiệu, vật liệu chung" (loại `notes`), không tách mỗi nhóm một sheet và không lập sheet bảng vật liệu riêng (KH8). Bảng gồm 5 nhóm, đúng thứ tự:

| # | Nhóm | Mỗi dòng gồm |
|---|---|---|
| KH1 | **Ký hiệu của Revit** dùng trong hồ sơ: room tag, level, trục, callout, mặt cắt, mặt đứng, view title, tag cửa đi / cửa sổ, tag tường, cao độ, độ dốc, mũi tên Bắc… | Ký hiệu đúng family / type đang dùng + tên gọi, ý nghĩa từng ô chữ trong ký hiệu |
| KH2 | **Sàn hoàn thiện** | Filled region đúng pattern của vật liệu sàn hoàn thiện + tên vật liệu |
| KH3 | **Trần** | Như KH2, cho vật liệu trần |
| KH4 | **Tường** | Như KH2, cho vật liệu hoàn thiện tường |
| KH5 | **Thiết bị**: chiếu sáng, quạt, cửa thông gió, AP… | Ký hiệu thiết bị như thể hiện trên mặt bằng + tên thiết bị |

- **KH6 — Chỉ ghi cái có thật**: mỗi dòng ứng với một ký hiệu, vật liệu hoặc thiết bị **đang được dùng** trong model / trên các sheet của bộ hồ sơ; tên lấy từ model (R8). Không chép cả thư viện; thứ có trên bản vẽ mà thiếu trong bảng là lỗi ĐỦ.
- **KH7 — Trình bày** (`drafting-work-rules.md` §2a): mỗi nhóm có tiêu đề; hai cột `KÝ HIỆU | DIỄN GIẢI`, ký hiệu chung một trục giữa, chữ chung mép trái, các dòng cách đều. Bảng dài hơn vùng vẽ → chia cột trên cùng sheet, chỗ cắt nằm giữa hai nhóm, các cột cùng mép trên.
- **KH8 — Gộp bảng vật liệu** (user, 2026-11-12): dòng "Bảng vật liệu / hoàn thiện chung" của bộ lõi (§4) không thành sheet riêng. KH2–KH4 chính là bảng vật liệu sàn, trần, tường (thêm mã vật liệu vào dòng khi hồ sơ dùng mã); vật liệu hoàn thiện khác đang dùng (mái, ốp mặt đứng, sân vườn…) thành nhóm `VẬT LIỆU KHÁC` ngay sau KH4, cùng hai cột, cùng đường gióng (KH7). Mỗi vật liệu chỉ ghi một lần.
- Ghi chú chung và chữ viết tắt đứng cùng sheet, trước bảng ký hiệu.

## 9. Xong khi (layout của bộ hồ sơ)

Bộ hồ sơ chỉ xong khi đủ tất cả các điều sau:
- Mọi bản vẽ bắt buộc (§1, §2, §4 và file bộ hồ sơ của loại) đều có view và đã nằm trên sheet; bản vẽ chi tiết vệ sinh, thang bộ, thang máy đủ view con theo CT1–CT3 (§4b). Bản vẽ nào còn thiếu thì đã ghi vào việc tồn kèm lý do.
- Số và tên sheet, tên view theo §3, §8 (TG1–TG5); danh mục bản vẽ và print set khớp nhau.
- Mọi sheet đạt LO1–LO12. Nếu có chia vùng thì đạt Z1–Z5.
- `viewport_title_audit` sạch; ảnh sheet đã soát (`drafting-visual-check`), không còn đè chữ hay tràn khung.

## 10. Tham khảo

- TCVN 5671:2012: Hệ thống tài liệu thiết kế xây dựng, hồ sơ thiết kế kiến trúc.
- TCVN 7285:2003 (khổ giấy, cách trình bày tờ giấy vẽ) và TCVN 7286:2003 (tỷ lệ bản vẽ).
- Quy định hiện hành về hồ sơ thiết kế xây dựng theo giai đoạn.

Quy định của dự án, của chủ đầu tư hoặc của cơ quan thẩm định **luôn ưu tiên** hơn chuẩn này. Ghi những khác biệt đó vào profile.

## 11. Sổ bộ hồ sơ (sheet register): nguồn sự thật của quy trình dàn trang

Sổ bộ hồ sơ là **một file** cho mỗi job: `sheet-register-<YYYY-MM-DD>.md`, đặt trong thư mục đợt (`review/<ngày>_<mã>/`) hoặc `log/`.
- Mỗi **dòng** là một bản vẽ của bộ hồ sơ mục tiêu (`drafting-sheet-layout` thẻ 01).
- Mỗi **skill chỉ điền các cột của mình**. Skill sau chỉ chạy trên các dòng mà skill trước đã đưa tới đúng trạng thái.
- Không skill nào được bỏ qua một dòng mà không ghi lý do.

| Cột | Ai điền (skill · thẻ) |
|---|---|
| `ID` (thứ tự trong bộ), `Nhóm`, `Bản vẽ`, `Tầng / vùng / mẫu`, `Tỷ lệ`, `GĐ`, `Nguồn` (dòng chuẩn), `Loại`, `Mức` (`drafting-coverage.md` §2) | `drafting-sheet-layout` 01 |
| `Có sẵn` (ok / chưa đặt / chưa có view / thừa) | `drafting-sheet-layout` 02 |
| `Khổ giấy`, `Vừa khổ`, `Vùng` | `drafting-sheet-layout` 03 |
| `Số sheet`, `Tên sheet`, `Sheet id` | `drafting-sheet-create` |
| `Loại view`, `Tên view`, `View id`, `Template`, `Crop` | `drafting-view-create` |
| `Template` (tên template thật, cho cả view có sẵn) | `drafting-view-template` |
| `Vị trí (mm)`, `Viewport id`, `Viewport type` | `drafting-sheet-place` |
| `Kiểm` (LO / Z đạt, hoặc mã lỗi) | `drafting-sheet-layout` 04 |
| `Trạng thái`, `Ghi chú` | mọi skill, khi đổi trạng thái |

**Trạng thái của mỗi dòng**, đi theo đúng thứ tự:

```text
planned → sheet → view → placed → checked
```

Các dấu đặc biệt:
- `!` = chưa xong, có lý do (vào Việc tồn);
- `n/a` = không áp dụng, có lý do;
- `extra` = có trong model nhưng ngoài bộ, user đã quyết giữ hay bỏ.

Một dòng có nhiều view (vd. chi tiết thang = mặt bằng các tầng + mặt cắt): mỗi view một dòng con `ID.a`, `ID.b`…, cùng một sheet.

Quy trình chỉ xong khi mọi dòng là `checked`, `n/a` hoặc `!` có lý do (§9).
