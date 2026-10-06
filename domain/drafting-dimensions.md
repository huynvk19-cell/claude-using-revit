---
name: drafting-dimensions
description: "Chuẩn dim: dim trục (grid), dim cao độ (level), dim cửa đi/cửa sổ/cửa cuốn trên mặt đứng và mặt cắt, type dim. Dimension standards."
metadata:
  updated: "2026-10-05"
  related: ["drafting-work-rules", "drafting-tools", "drafting-api-pitfalls"]
---

# Chuẩn dim (Dimension standards)

## 1. Type dim

- Khi review: dùng **type kiểm tra** (có màu, theo profile).
- Khi user duyệt ("trở về màu dim chính thức"): đổi sang **type chính thức** bằng `swap_dim_type`.
- Sau khi duyệt xong: xoá các type, tham số, schedule dùng để kiểm tra.
- Tra id của type ngay trong lượt làm việc. Không ghi cứng id.

## 2. Dim trục (Grid dims)

**Thành phần**: một chuỗi trục-trục (chain) và một dim tổng (overall, từ trục đầu đến trục cuối).

**Vị trí**
- Ngay phía trong đầu trục (bubble), **bên ngoài công trình**.
- Chữ của dim tổng nằm đè lên đường trục → dời dọc theo dim sao cho mép chữ cách trục 0.5–1 mm.

**Dim một phía hay hai phía**
- Chật hoặc vướng → chỉ dim một phía.
- Phía nào là theo profile hoặc theo comment.

**Không dim trục** (mặc định, profile có thể thêm):
- view STAIRCASE
- view tiện ích điển hình: toilet, utility
- dải hẹp của partial plan (1/2, 2/2)

**Các trường hợp khác**
- Trục trùng vị trí (khai báo trong profile) → tính là một.
- **Không** tham chiếu trục của link. Dim đang trỏ vào trục link → xoá, dim lại bằng trục host.
- Dependent view:
  - Dim thuộc view cha.
  - Dim phải nằm **trong crop của từng dependent** thì mới hiện.
  - Không muốn hiện ở dependent kia → ẩn riêng trong view đó.
- Mặt cắt: dim trục đặt ở phía bubble, kèm dim cao độ.

## 3. Dim cao độ (Level dims) — mặt cắt / mặt đứng

- Gồm chuỗi giữa các level đang hiện và dim tổng (level thấp nhất → cao nhất).
- Đặt cạnh đầu level, phía trong 2D extent.
- Tool: `level_dims_add`.

## 4. Dim cửa sổ, cửa đi (Opening dims)

Đã tách ra file riêng: **`drafting-opening-dims.md`**. Gồm 5 quy tắc Q1–Q5: đủ dim đứng và dim ngang; host level → bệ → đỉnh; trục → mép → mép → trục; dim sát cửa; chỉ tham chiếu vào cửa.
