---
name: drafting-annotation
description: "Chuẩn đặt tag cửa đi/cửa sổ trên mặt đứng-mặt cắt, room tag, cao độ sàn và ký hiệu độ dốc trên mặt bằng, view title trên sheet. Tag, room tag, floor spot, slope mark and viewport title standards."
metadata:
  updated: "2026-10-17"
  related: ["drafting-work-rules", "drafting-views-sheets", "drafting-tools"]
---

# Chuẩn ghi chú (Annotation standards)

## 1. Tag cửa đi / cửa sổ — mặt đứng, mặt cắt

Đã tách ra file riêng **`drafting-opening-tags.md`** (quy tắc T1–T8) và skill `drafting-opening-tags`.

## 2. Room tag

### Đặt tag room (RT1–RT8, user 2026-10-15, RT5–RT8 2026-10-21)

Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng trần (`drafting-ceiling-plan`), thẻ bước "Tag room". Lệnh: `room_tags_missing` (room chưa tag + vị trí theo RT1–RT8) → `roomtag_place_ortho`.

| # | Quy tắc |
|---|---|
| RT1 | **Type (family : type) do người dùng chọn.** Yêu cầu **bổ sung** (tag các room còn thiếu trên view đã có tag) → dùng type của các room lân cận (type phổ biến nhất trong 3 room gần nhất đã tag). Việc mới mà chưa có type → hỏi, không tự chọn. |
| RT2 | **Không cắt**: tag không cắt qua tường, cạnh của sàn (mặt bằng trần: cạnh của trần); không đè dim, tag, text. |
| RT3 | **Vị trí**: tag (hoặc chấm của leader) đặt gần **giao điểm hai đường chéo** của room (tâm hình chữ nhật bao room theo khung view). Room chữ L có giao điểm nằm ngoài room → điểm gần nhất trong room. |
| RT4 | **Trong hay ngoài**: room đủ chỗ chứa tag mà không vi phạm RT2 → đặt **trong** room, không leader, gần giao điểm. Không đủ → đặt **ngoài** room, **có leader** đi thẳng, bẻ góc vuông khi cần so với khổ giấy (mục "Leader của tag" bên dưới), chấm của leader gần giao điểm hai đường chéo; chọn leader ngắn nhất. |
| RT5 | **Thứ tự**: tag lần lượt từ room có **diện tích nhỏ nhất** trên view tới room lớn nhất (room nhỏ khó chỗ được chọn trước). |
| RT6 | **Đủ 100%**: mọi room thấy trên view đều có tag, không bỏ sót room nào. Không có chỗ sạch gần → leader dài hơn, rồi leader một góc vuông, rồi mới chấp nhận chạm nét; vẫn phải đặt và ghi vào Cần xem. Chỉ room không đặt (diện tích 0 / không kín) mới không tag được → báo user. |
| RT7 | **Leader ngắn**: luôn ưu tiên vị trí có leader ngắn nhất (thẳng trước, một góc vuông sau). |
| RT8 | **Thẳng hàng** (ưu tiên): tag gần nhau cùng hàng (cùng tâm đứng) hoặc cùng cột (cùng mép trái); khi hai chỗ gần ngang nhau về độ dài leader, chọn chỗ thẳng hàng với tag đã đặt. |

**Điểm tag room (RS1–RS4, user 2026-10-21)** — mỗi tag bắt đầu 10 điểm, tối thiểu 0; điểm view = trung bình các tag (`room_tags_score`).

| # | Trừ điểm |
|---|---|
| RS1 | Leader trùng đè lên leader khác → **0 điểm ngay** |
| RS2 | Leader bẻ góc vuông → −5 |
| RS3 | Leader dài hơn 3 lần cạnh dài của room → −9; dài hơn 2 lần → −5 |
| RS4 | Tag nằm trên room khác → −5 |

`room_tags_missing` chọn chỗ có điểm cao nhất (rồi mới tới leader ngắn, thẳng hàng).

Kích thước tag trên giấy (rộng × cao mm) lấy từ `drafting-profile.md`; không có thì lệnh lấy hộp bao của một tag cùng type (rộng hơn chữ, kết quả thiên về an toàn).

### Sửa tag room

| Tình huống | Xử lý |
|---|---|
| Tag nằm ngoài phòng của nó, không có leader chỉ vào phòng | Dời về location point của room, tắt leader (`room_tags_outside`). Tag ngoài phòng có leader chỉ vào phòng là đặt theo RT4, giữ nguyên. |
| Tag hiện "?" (room không có tên/số) | Tô đỏ bằng view override (`room_tags_broken`), báo user sửa model. **Không** tự sửa room. |

## 2b. Cao độ sàn trên mặt bằng (FL1–FL3, FL1b, user 2026-10-16)

Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng hoàn thiện sàn (`drafting-plan-floor-finish`), thẻ bước "Tag cao độ sàn". Lệnh: `room_spots_missing` (phòng chưa có cao độ + điểm đặt) → `annot_place`; buồng thang: `stair_door_spots`.

| # | Quy tắc |
|---|---|
| FL1 | **Mỗi gian phòng có 1 cao độ sàn** (Spot Elevation trên mặt sàn của phòng). Trừ gian phòng **nằm bên trong một gian phòng khác** mà diện tích không đáng kể (ngưỡng m² trong profile, mặc định 5 m²) → không cần. Phòng đã có một cao độ bên trong → không thêm. |
| FL1b | **Phòng có nhiều cao độ sàn** (không có sàn chung bên dưới mà gồm nhiều sàn nhỏ chênh cao độ, có ramp dốc): cao độ **mỗi mặt sàn phẳng** (mỗi cao độ một cái) và **hai đầu mỗi ramp** (đầu thấp, đầu cao), trừ đầu ramp đã chạm sàn phẳng cùng cao độ. Điểm đầu ramp đặt sát mép đầu (1 mm) để số đọc đúng cao độ đầu ramp. `room_spots_missing` tự nhận ra (`Levels`). User, 2026-10-16. |
| FL2 | **Buồng thang bộ: cao độ bên trong và bên ngoài** mỗi cửa buồng thang, kể cả khi hai bên cùng cao độ (như SB3 của `drafting-stair-plan.md`, user 2026-10-15). Ra ngoài nhà ở tầng trệt: ký hiệu cao độ hiện trạng của profile nếu dự án dùng. |
| FL3 | **Theo ý người dùng**: type cao độ do người dùng chọn (dự án cố định thì lấy trong profile); việc bổ sung trên view đã có cao độ → type đang dùng nhiều nhất trên view. Chỗ người dùng đã đặt tay thì giữ nguyên. Chưa có type → hỏi, không tự chọn. |

Vị trí: trong phòng, gần giao điểm hai đường chéo (như RT3), không đè tường, cạnh sàn, dim, tag (kể cả room tag), text; không trên cung mở cửa. Room tag đặt trước, cao độ tránh nó.

## 2c. Ký hiệu độ dốc trên mặt bằng (SL1–SL5, user 2026-10-17)

Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng hoàn thiện sàn (`drafting-plan-floor-finish`), thẻ bước "Ký hiệu độ dốc". Lệnh: `floor_levels_probe` (mặt dốc trên view: độ dốc %, hướng dốc) → `slope_spots_place` (đặt Spot Slope). Revit 2023 API không có hàm tạo spot slope: lệnh chép một spot slope **có sẵn trên một mặt bằng** rồi để Revit gắn vào mặt dưới điểm mới; dự án chưa có cái nào trên mặt bằng → nhờ user đặt tay một cái làm mẫu (Annotate > Spot Slope).

| # | Quy tắc |
|---|---|
| SL1 | **Chỗ cần ký hiệu**: mái, sàn có chênh lệch cao độ, ramp dốc, nói chung mọi mặt bằng có thay đổi cao độ. Mỗi **mặt dốc** thấy trên view (mặt trên của sàn / ramp / mái / chiếu nghỉ có độ dốc > 0) có một ký hiệu độ dốc. Không tính: mặt phẳng ngang, mặt đứng hay mép (độ dốc ≥ 50 %, diện tích < 1 m²), phần tử link. |
| SL2 | **Type có hướng xuống**: Spot Slope type có `Slope Direction` = **Down** (mũi tên chỉ chiều dốc xuống). |
| SL3 | **Đơn vị**: chỉ số độ dốc là **số thập phân** hoặc **phần trăm** (đơn vị của type, hoặc đơn vị dự án khi type dùng mặc định). Không dùng độ (°) hay tỉ lệ 1:n. |
| SL4 | **Type có sẵn**: dùng type có sẵn thoả SL2 + SL3 (type profile cố định → type thoả đang dùng nhiều nhất → type thoả bất kỳ). Không có type nào thoả → hỏi user trước khi tạo type mới; không đổi tham số của type có sẵn. Ký hiệu mới mang type đánh dấu. |
| SL5 | **Vị trí**: trên mặt dốc, gần tâm mặt; mũi tên theo hướng dốc của mặt (Revit tự lấy). Không đè tường, dim, tag, room tag, cao độ, text; mặt dài có tag / cao độ ở giữa → dời dọc theo mặt tới chỗ trống. Mặt quá nhỏ so với chiều dài leader của type (vd. 20 mm giấy) → báo user. |

Kiểm tra sau khi làm: mỗi mặt dốc SL1 có một spot slope (hoặc ghi lý do); `view_elem_boxes`: Box ≠ null; `annotation_overlaps` sạch; ảnh xuất: mũi tên chỉ xuống dốc, chữ đọc được.

## 3. View title (viewport title)

**Vị trí**
- Hiện title, căn giữa theo bản vẽ (tâm crop).
- Đặt ngay dưới nội dung thấp nhất: crop, đầu grid/level, dim.
- Tool: `viewport_titles_place`.

**Khi nào làm lại**
- Sau khi đổi grid, dim hoặc crop → căn lại title.

**Ngoại lệ**
- Title của view cầu thang để nguyên, trừ khi comment yêu cầu.

**Kiểm tra**: `viewport_title_audit`.

## Leader của tag (mọi loại tag, kể cả room tag, user 2026-10-07, 2026-10-15)

- Leader **đi đường thẳng, bẻ góc vuông khi cần**, ngang / dọc **so với khổ giấy** (cạnh của sheet). Đầu tag thẳng hàng với điểm chạm → một đoạn thẳng; không thì đúng một điểm gấp (dọc rồi ngang, hoặc ngang rồi dọc). Không có leader xiên.
- Khung view (Right / Up) trùng phương khổ giấy (viewport chỉ xoay 0° / 90° / 270°), nên ngang / dọc theo khung view là ngang / dọc trên giấy.
- Điểm cuối chạm đúng mép chủ thể.
- Tool: `annot_place` tự thêm điểm gấp (`elbowFirst` V mặc định / H, hoặc `elbowRight/elbowUp`), cả với room tag; kiểm tra bằng `tag_leaders_info` (cả room tag; cột shape: V, H, V+H, H+V; `D` = xiên → sửa).
