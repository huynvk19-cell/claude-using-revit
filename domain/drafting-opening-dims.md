---
name: drafting-opening-dims
description: "Chuẩn dim cửa sổ, cửa đi (kể cả cửa cuốn) trên mặt đứng và mặt cắt: dim đứng host level → bệ → đỉnh, dim ngang trục → mép → mép → trục, vị trí sát cửa, chỉ tham chiếu vào cửa. Door/window dimension standard for elevations and sections."
metadata:
  updated: "2026-10-06"
  related: ["drafting-dimensions", "drafting-annotation", "drafting-tools", "drafting-api-pitfalls"]
---

# Dim cửa sổ, cửa đi — mặt đứng và mặt cắt

## 1. Năm quy tắc (từ user, 2026-10-06)

| # | Quy tắc |
|---|---|
| Q1 | **Mỗi cửa sổ, cửa đi luôn có đủ 2 dim**: một dim **đứng** (chiều cao) và một dim **ngang** (khoảng cách). |
| Q2 | **Dim đứng** đi theo chuỗi: **host level → bệ cửa → đỉnh cửa**. Đọc dim là biết cửa cao bao nhiêu và đặt cao bao nhiêu. |
| Q3 | **Dim ngang** đi theo chuỗi: **trục → mép cửa → mép cửa → trục**. Đọc dim là biết cửa rộng bao nhiêu và cách trục bao nhiêu. |
| Q4 | **Dim đặt gần cửa nhất có thể.** |
| Q5 | **Dim phải tham chiếu vào chính cửa sổ / cửa đi**, không tham chiếu vào vật khác. |

## 2. Chi tiết từng quy tắc

### Q1 — Đủ 2 dim cho mỗi cửa

- "Mỗi cửa" là mỗi cửa **thật sự nhìn thấy** trong view. Cửa nằm sau tường, sau kính hoặc sau ô cửa khác thì **không** dim (tránh lỗi "dim 50/2135/2250").
- Coi là **đã có** khi một dim sẵn có thoả cả 3 điều:
  - tham chiếu đúng vào cửa (Q5);
  - cho đúng chuỗi (Q2, Q3);
  - nằm sát cửa (Q4).
  Khi đó không thêm dim thứ hai.
- Dim đen đang trỏ vào mặt tường hay mép lỗ tường thì **không tính**. Thêm dim đúng, rồi báo user dim cũ có cần xoá không.

### Q2 — Dim đứng

**Level dùng để dim**
- Phải là **host level của cửa** (tham số Level / Schedule Level của instance).
- **Không** lấy "level gần nhất phía dưới".
- Cửa đặt trên mezzanine thì host level là level mezzanine.

**Theo loại cửa**

| Trường hợp | Chuỗi dim | Ví dụ |
|---|---|---|
| Cửa sổ | host level → bệ (sill) → đỉnh (head) | 1095 · 1550 |
| Cửa đi có ngưỡng hoặc offset ≥ 150 mm | host level → đáy → đỉnh | |
| Cửa đi đặt sát level (offset < 150 mm) | host level → đỉnh | 2250 |
| Cửa cuốn | host level → named reference **TOP** của family | 7000 |

- Cửa đi sát level: đoạn ngưỡng 0–150 mm bị bỏ, vì Revit không dim được đoạn 0 và đoạn 50 mm chỉ gây rối.
- Cửa cuốn: **không** lấy hộp cuốn, **không** lấy mặt khung.

**Trường hợp đặc biệt**
- Host level không hiện trong view → dim **đáy → đỉnh** và báo user.
- **Mỗi cửa một chuỗi đứng.** Cửa giống nhau đứng cạnh nhau vẫn mỗi cửa một chuỗi, trừ khi user cho phép dim đại diện.

### Q3 — Dim ngang

- **Trục**: lấy trục host gần nhất ở bên trái và bên phải cửa (trong khoảng ~12 m). Không lấy trục link.
- **Mép cửa**: mép ngoài khung cửa. Cửa cuốn lấy named reference **LEFT → RIGHT** (bề rộng danh nghĩa).
- **Nhiều cửa cùng hàng giữa hai trục**: dùng **một chuỗi chung**, ví dụ `trục → mép → mép → mép → mép → trục`.
- **Không có trục ở một phía** (đầu hồi, ngoài phạm vi trục) → chuỗi dừng ở mép cửa cuối, rồi báo user.
- Chuỗi nối hai cửa cách nhau rất xa (có đoạn > ~50 m) → **tách** thành từng chuỗi riêng, mỗi chuỗi có trục của nó.

### Q4 — Dim nằm sát cửa

**Dim đứng**
- Đặt ngay cạnh mép cửa: đường dim cách mép ~2–3 mm giấy.
- Ưu tiên phía không có cửa khác hoặc tag.
- Vướng thì lùi ra từng bước nhỏ (~1 chiều cao chữ).
- **Không** vẽ dim đứng đè lên chính cửa.

**Dim ngang**
- Đặt trên đường trống **đầu tiên** ngay trên đỉnh hoặc ngay dưới bệ của hàng cửa.
- Phải nằm **trong tầng của hàng đó**: không vượt qua sàn, không vượt qua hàng cửa khác.
- Ở mặt cắt, các hàng phía trên đặt dim **phía trên** cửa.

**Mức ưu tiên khi xếp chỗ**
- Gần cửa quan trọng hơn thẳng hàng với dim khác.
- Nhưng chữ không được đè lên chữ, tag hay dim khác.

**Hết chỗ** trong phạm vi ~3.5 m quanh hàng cửa → **không** đẩy dim ra xa (ra ngoài công trình, lên mái). Báo user để đặt tay.

### Q5 — Chỉ tham chiếu vào cửa

**Được tham chiếu**
- Reference của **family instance cửa**: named reference (TOP / BOTTOM / LEFT / RIGHT), hoặc mặt khung của chính cửa đó.
- **Host level** và **trục host**.

**Không được tham chiếu**
- mặt tường, mép lỗ mở của tường;
- mép sàn hoặc dầm;
- detail line;
- hình học trong file link;
- cửa của link.

**Kiểm tra sau khi tạo**: mỗi đoạn dim phải có ít nhất một đầu nằm trên cửa, trừ đoạn level → bệ và đoạn trục → mép.

## 3. Quy định chung

- Type dim: dùng **type kiểm tra** cho tới khi user duyệt (xem `drafting-dimensions.md`).
- **Mỗi lần gọi một view**, có `timeoutSeconds`.
- Chỉ thêm dim. Không sửa cửa, tường, level.
