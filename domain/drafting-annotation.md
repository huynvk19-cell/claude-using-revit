---
name: drafting-annotation
description: "Chuẩn đặt tag cửa đi/cửa sổ trên mặt đứng-mặt cắt, room tag, view title trên sheet. Tag, room tag and viewport title standards."
metadata:
  updated: "2026-10-15"
  related: ["drafting-work-rules", "drafting-views-sheets", "drafting-tools"]
---

# Chuẩn ghi chú (Annotation standards)

## 1. Tag cửa đi / cửa sổ — mặt đứng, mặt cắt

Đã tách ra file riêng **`drafting-opening-tags.md`** (quy tắc T1–T8) và skill `drafting-opening-tags`.

## 2. Room tag

### Đặt tag room (RT1–RT4, user 2026-10-15)

Áp dụng cho mặt bằng kích thước (`drafting-plan-dims`) và mặt bằng trần (`drafting-ceiling-plan`), thẻ bước "Tag room". Lệnh: `room_tags_missing` (room chưa tag + vị trí theo RT1–RT4) → `annot_place`.

| # | Quy tắc |
|---|---|
| RT1 | **Type (family : type) do người dùng chọn.** Yêu cầu **bổ sung** (tag các room còn thiếu trên view đã có tag) → dùng type của các room lân cận (type phổ biến nhất trong 3 room gần nhất đã tag). Việc mới mà chưa có type → hỏi, không tự chọn. |
| RT2 | **Không cắt**: tag không cắt qua tường, cạnh của sàn (mặt bằng trần: cạnh của trần); không đè dim, tag, text. |
| RT3 | **Vị trí**: tag (hoặc chấm của leader) đặt gần **giao điểm hai đường chéo** của room (tâm hình chữ nhật bao room theo khung view). Room chữ L có giao điểm nằm ngoài room → điểm gần nhất trong room. Room bị crop cắt, không thấy giao điểm → chấm leader ở vị trí hợp lý trong phần room nhìn thấy (tâm phần nhìn thấy), user 2026-10-15. |
| RT4 | **Trong hay ngoài**: room đủ chỗ chứa tag mà không vi phạm RT2 → đặt **trong** room, không leader, gần giao điểm. Không đủ → đặt **ngoài** room, **có leader** đi thẳng, bẻ góc vuông khi cần so với khổ giấy (mục "Leader của tag" bên dưới), chấm của leader gần giao điểm hai đường chéo; chọn leader ngắn nhất. Gần đó không có chỗ trống → dời tag ra xa hợp lý, chỉ cần có leader dẫn tới room (user 2026-10-15). |

Kích thước tag trên giấy (rộng × cao mm) lấy từ `drafting-profile.md`; không có thì lệnh lấy hộp bao của một tag cùng type (rộng hơn chữ, kết quả thiên về an toàn).

### Sửa tag room

| Tình huống | Xử lý |
|---|---|
| Tag nằm ngoài phòng của nó, không có leader chỉ vào phòng | Dời về location point của room, tắt leader (`room_tags_outside`). Tag ngoài phòng có leader chỉ vào phòng là đặt theo RT4, giữ nguyên. |
| Tag hiện "?" (room không có tên/số) | Tô đỏ bằng view override (`room_tags_broken`), báo user sửa model. **Không** tự sửa room. |

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
