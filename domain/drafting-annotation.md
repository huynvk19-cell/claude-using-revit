---
name: drafting-annotation
description: "Chuẩn đặt tag cửa đi/cửa sổ trên mặt đứng-mặt cắt, room tag, cao độ sàn và ký hiệu độ dốc trên mặt bằng, view title trên sheet. Tag, room tag, floor spot, slope mark and viewport title standards."
metadata:
  updated: "2026-10-22"
  related: ["drafting-work-rules", "drafting-views-sheets", "drafting-tools"]
---

# Chuẩn ghi chú (Annotation standards)

## 1. Tag cửa đi / cửa sổ — mặt đứng, mặt cắt

Đã tách ra file riêng **`drafting-opening-tags.md`** (quy tắc T1–T8) và skill `drafting-opening-tags`.

## 2. Room tag

### Đặt tag room và chấm điểm (TR1–TR8, Huy 2026-10-22)

Viết lại từ đầu ngày 2026-10-22: toàn bộ luật cũ RT1–RT8 và RS0–RS5 đã xoá. Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng trần (`drafting-ceiling-plan`), thẻ bước "Tag room". Lệnh: `room_tags_missing` (room chưa tag + chỗ đặt) → `roomtag_place_ortho` (đặt) → `room_tags_score` (chấm).

**Đặt**: Claude tìm **đường leader ngắn nhất** cho mỗi tag. Room chứa được thân tag → đặt trong room, không leader (leader dài 0 = điểm tối đa). Không chứa được → thân tag ở ngoài, leader thẳng hoặc một góc vuông (mục "Leader của tag"), chấm leader nằm ngay trong room (cách biên room `insetMm`, mặc định 2 mm giấy) ở chỗ leader vào room sớm nhất. Trong các chỗ hợp lệ, chọn chỗ điểm cao nhất, bằng điểm thì leader ngắn hơn.

**Điểm mỗi tag** — tối đa `maxScore` (mặc định 10), kẹp trong 0…tối đa:

| # | Luật |
|---|---|
| TR1 | **Leader dài 0, tag nằm trong room của nó → điểm tối đa**; giảm đều về 0 khi leader dài tới `zeroAtMm` (mặc định 50 mm giấy): điểm = tối đa × (1 − L / zeroAtMm). L đo trên giấy từ mép thân tag tới chấm; tag không leader nằm ngoài room → L = khoảng cách từ tâm thân tag tới room. |
| TR2 | Tag **thẳng hàng** (cùng tâm đứng = cùng hàng) hoặc **thẳng cột** (cùng mép trái hoặc cùng trục giữa) với tag room khác trong 80 mm giấy → **+2** cho tag đó. |
| TR3 | Thân tag **nằm trên tường, cạnh sàn** (mặt bằng trần: **cạnh trần**) → **0**. |
| TR4 | Đường đi của leader **trùng** đường đi của leader khác → **0**. |
| TR5 | Đường đi của leader **trùng tường, cạnh sàn / cạnh trần** (chạy dọc theo nét) → **0**. Cắt ngang tường để vào room thì được. |
| TR6 | Leader **đi qua điểm cuối (chấm) của leader khác** → **0**. |
| TR7 | Leader **đi qua vùng của room khác** → **−3 mỗi room** (mọi room trừ room của nó, kể cả room mà thân tag đang nằm lên). |
| TR8 | **Thiếu tag** của bất kỳ room nào thấy trên view → **điểm thẻ của view = 0**. |

Điểm thẻ của view = trung bình điểm các tag (TR8 thì 0). **Đạt khi > 7** (`acceptAbove`). Thang 0–10 và ngưỡng 7 là mặc định đang chờ Huy chốt.

**Lệnh không bao giờ chọn** chỗ dính TR3–TR6, và không cho leader khác đi qua chấm của tag mới; thân tag giữ cách tường, cạnh sàn / trần, dim, tag, text, bubble trục `clearMm` (mặc định 1 mm giấy), leader không chui qua tag / text (chỉ nới ở bước cuối, ghi CHECK). Không có chỗ trong `zeroAtMm` → tìm xa tới 2 lần (TR1 = 0); vẫn không có → đặt ở tâm room, ghi FORCED: room nào cũng có tag (TR8).

Type tag: `typeName` trong profile; không có → hỏi user một lần (không tự chọn); yêu cầu bổ sung trên view đã có tag → type của các tag gần nhất. Kích thước thân tag trên giấy (rộng × cao mm) lấy từ `drafting-profile.md` (`tagSizeMm`); không có thì lệnh đo hộp của một tag cùng type (rộng hơn chữ).

### Sửa tag room

| Tình huống | Xử lý |
|---|---|
| Tag nằm ngoài phòng của nó, không có leader chỉ vào phòng | Dời về location point của room, tắt leader (`room_tags_outside`). Tag ngoài phòng có leader chỉ vào phòng là tag có leader (TR1), giữ nguyên. |
| Tag hiện "?" (room không có tên/số) | Tô đỏ bằng view override (`room_tags_broken`), báo user sửa model. **Không** tự sửa room. |

## 2b. Cao độ sàn trên mặt bằng (FL1–FL3, FL1b, user 2026-10-16)

Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng hoàn thiện sàn (`drafting-plan-floor-finish`), thẻ bước "Tag cao độ sàn". Lệnh: `room_spots_missing` (phòng chưa có cao độ + điểm đặt) → `annot_place`; buồng thang: `stair_door_spots`.

| # | Quy tắc |
|---|---|
| FL1 | **Mỗi gian phòng có 1 cao độ sàn** (Spot Elevation trên mặt sàn của phòng). Trừ gian phòng **nằm bên trong một gian phòng khác** mà diện tích không đáng kể (ngưỡng m² trong profile, mặc định 5 m²) → không cần. Phòng đã có một cao độ bên trong → không thêm. |
| FL1b | **Phòng có nhiều cao độ sàn** (không có sàn chung bên dưới mà gồm nhiều sàn nhỏ chênh cao độ, có ramp dốc): cao độ **mỗi mặt sàn phẳng** (mỗi cao độ một cái) và **hai đầu mỗi ramp** (đầu thấp, đầu cao), trừ đầu ramp đã chạm sàn phẳng cùng cao độ. Điểm đầu ramp đặt sát mép đầu (1 mm) để số đọc đúng cao độ đầu ramp. `room_spots_missing` tự nhận ra (`Levels`). User, 2026-10-16. |
| FL2 | **Buồng thang bộ: cao độ bên trong và bên ngoài** mỗi cửa buồng thang, kể cả khi hai bên cùng cao độ (như SB3 của `drafting-stair-plan.md`, user 2026-10-15). Ra ngoài nhà ở tầng trệt: ký hiệu cao độ hiện trạng của profile nếu dự án dùng. |
| FL3 | **Theo ý người dùng**: type cao độ do người dùng chọn (dự án cố định thì lấy trong profile); việc bổ sung trên view đã có cao độ → type đang dùng nhiều nhất trên view. Chỗ người dùng đã đặt tay thì giữ nguyên. Chưa có type → hỏi, không tự chọn. |

Vị trí: trong phòng, gần giao điểm hai đường chéo của room (tâm hình chữ nhật bao room theo khung view), không đè tường, cạnh sàn, dim, tag (kể cả room tag), text; không trên cung mở cửa. Room tag đặt trước, cao độ tránh nó.

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
