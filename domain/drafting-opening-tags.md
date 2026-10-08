---
name: drafting-opening-tags
description: "Chuẩn tag cửa sổ, cửa đi (kể cả cửa cuốn) trên mặt đứng và mặt cắt: mỗi cửa nhìn thấy có đúng một tag, đặt phía trên-căn giữa-sát cửa, leader ngắn, cách xử lý khi vướng, không đè dim/tag/cấu kiện. Door/window tag standard for elevations and sections."
metadata:
  updated: "2026-10-06"
  related: ["drafting-opening-dims", "drafting-annotation", "drafting-tools", "drafting-api-pitfalls"]
---

# Tag cửa sổ, cửa đi — mặt đứng và mặt cắt

Các quy tắc dưới đây rút ra từ những lần user tự chỉnh lại tag (đợt ES-A, 2026-10-02) và từ comment review đợt LG.

## 1. Quy tắc

### T1 — Đủ và đúng tag

| Tình huống | Cách làm |
|---|---|
| Cửa **thật sự nhìn thấy** trong view | Có **đúng một** tag |
| Cửa bị che (sau tường, sau kính, sau cửa khác) | **Không** tag |
| Mặt cắt: cửa nằm **trong tường bị cắt** (thấy mặt cắt khung cửa, không thấy chính diện) | **Có** tag (user, 2026-10-08): đầu tag ngoài tường, leader **ngang** vào cửa ở khoảng giữa chiều cao cửa. `elevation_opening_tags` bỏ sót loại cửa này → kiểm tay |
| View OVERALL (tỉ lệ nhỏ) | **Không** tag cửa (và không dim cửa) |
| Hai tag trên cùng một cửa | Xoá bớt một (ví dụ hai tag "252" trên cùng cửa) |
| Tag mồ côi, tag hiện "?" hoặc rỗng | Báo user |

### T2 — Vị trí mặc định

- Tag nằm **ngay phía trên** cửa của nó.
- Căn giữa theo cửa, sát mép cửa (cách khoảng 1 mm giấy).
- Leader đứng ngắn, đi vào trong cửa, đầu leader cách mép cửa khoảng **1.5 mm giấy**.
- Mặt cắt chi tiết (vd. mặt cắt thang): tag cửa **luôn có leader** (user, 2026-10-08; mẫu 1:50: đầu tag trên đỉnh cửa ~190 mm, đầu leader trong cửa ~130 mm). Chạy `elevation_opening_tags` với `addLeader:true`.
- Đầu tag **không** nằm đè lên chính cửa của nó. Ngoại lệ duy nhất là cửa cuốn lớn, xem T5.

### T3 — Cửa sổ

- Tag cửa sổ có thể đặt **phía trên hoặc phía dưới** cửa. Ưu tiên phía trên.
- Hai cửa sổ xếp chồng nhau:
  - tag của cửa trên đặt **phía trên cửa trên**;
  - không đặt hai tag vào cùng một khe giữa hai cửa.
- **Không** đặt tag cửa sổ ở bên cạnh cửa.

### T4 — Cửa đi bị vướng ngay phía trên

Vật vướng có thể là dầm, poche sàn hoặc dim. Xử lý theo thứ tự:

1. Nâng tag lên mảng tường sạch phía trên, tối đa khoảng **10 mm giấy**, kéo leader dài ra. User từng dùng 6 mm và 9.8 mm.
2. Vẫn vướng (ví dụ cửa hẹp sát cầu thang, nhiều dim): đặt tag **bên cạnh** cửa, ngang đỉnh cửa, dùng leader ngang đi vào cửa.

### T5 — Cửa cuốn (DOR - ROLL UP)

**Tag được đặt nằm trong cửa** (user, 2026-10-06), khi tag quá nhỏ so với cửa:

| Điều kiện | Ngưỡng |
|---|---|
| Chiều rộng đầu tag | ≤ **1/3** chiều rộng nhìn thấy của cửa |
| Chiều cao đầu tag | ≤ **1/4** chiều cao nhìn thấy của cửa |

Ví dụ: cửa 4000–7000 mm ở tỉ lệ 1:200 là 20–35 mm giấy, tag cỡ 6–8 mm.

Cách đặt khi tag nằm trong cửa:
- Đặt **trong lòng cửa**, căn giữa theo chiều ngang, nằm khoảng giữa chiều cao cửa.
- **Không** dùng leader.
- Vẫn phải tránh: dim đứng/ngang của chính cửa, chữ dim, tag khác, room tag, nét chéo ký hiệu đóng/mở nếu che số.
- Các cửa cuốn cùng hàng thì tag cùng cao độ (T7).
- Tag đã nằm trong cửa và thoả điều kiện trên → **giữ nguyên**, không coi là "tag đè lên cửa".

Cửa cuốn nhỏ (không thoả điều kiện) → đặt như cửa đi (T2, T4):
- Đầu leader nằm khoảng **2.5 mm giấy** bên trong **đỉnh nhìn thấy** của cửa.
- Hộp cuốn nằm sau tường **không** tính là đỉnh cửa.

### T6 — Không đè lên vật khác

| Loại | Gồm | Được đè? |
|---|---|---|
| Vật cứng | Dim (đường và chữ), tag khác, text note, cầu thang, lan can, thang máy, thiết bị, cột, dầm, cửa khác | **Không** |
| Vật mềm | Nét level/grid (nét đứt), sàn/mái nhìn thấy, curtain panel | Được |
| Dầm link đè lên hàng cửa sổ (dầm vành phía trên hàng cửa sổ trên cùng, louvre) | | Được, user chấp nhận |
| Room tag chồng nhau trong mặt cắt | | Được, user chấp nhận |
| Dầm đè lên **tag cửa đi** | | **Không** |

### T7 — Cùng hàng thì cùng cao độ

- Các tag của một hàng cửa giống nhau đặt **cùng cao độ** đầu tag.
- Căn theo một tag mẫu.

### T8 — Tôn trọng chỗ user đã chỉnh tay

- **Không** chạy lại "đặt lại toàn bộ" (`repositionAll`) trên view mà user đã tự chỉnh tag.
- Chỉ xử lý tag thiếu hoặc tag sai.

## 2. Loại tag

- Mặc định dùng loại tag cửa đi / cửa sổ được dùng **nhiều nhất** trong view.
- Không tạo type mới.
- Tên type và tham số không được chứa "Claude" hay "AI".

## 3. Kiểm tra sau khi làm

- Mỗi cửa nhìn thấy có đúng 1 tag (T1).
- Không còn tag nào nằm trên chính cửa của nó, đè dim hoặc đè cấu kiện cứng (T2, T6).
- Ảnh xuất sheet nhìn gọn: các tag cùng hàng thẳng hàng, leader ngắn, không chồng chữ.
- Tag còn vướng → tô đỏ để user xem.
- User duyệt xong → bỏ màu đỏ (clearhighlight).
