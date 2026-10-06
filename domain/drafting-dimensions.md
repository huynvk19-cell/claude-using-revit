---
name: drafting-dimensions
description: "Chuẩn dim: type dim, dim cao độ (level); trỏ sang chuẩn dim trục (drafting-grid-dims) và dim cửa (drafting-opening-dims). Dimension standards."
metadata:
  updated: "2026-10-06"
  related: ["drafting-work-rules", "drafting-grid-dims", "drafting-tools", "drafting-api-pitfalls"]
---

# Chuẩn dim (Dimension standards)

## 1. Type dim

- Khi review: dùng **type kiểm tra** (có màu, theo profile).
- Khi user duyệt ("trở về màu dim chính thức"): đổi sang **type chính thức** bằng `swap_dim_type`.
- Sau khi duyệt xong: xoá các type, tham số, schedule dùng để kiểm tra.
- Tra id của type ngay trong lượt làm việc. Không ghi cứng id.

## 2. Dim trục (Grid dims)

Đã tách ra file riêng: **`drafting-grid-dims.md`**. Gồm 4 quy tắc G1–G4:
- trục song song là một nhóm;
- mỗi nhóm trên mỗi bản vẽ có đúng 1 dim cách trục + 1 dim tổng, đặt cùng một phía;
- dim nằm giữa mép crop và bubble;
- annotation crop kéo ra tới bubble.

(2026-10-06: thay cho quy định cũ "phía trong bubble, bên ngoài công trình, một hoặc hai phía".)

**Không dim trục** (mặc định, profile có thể thêm):
- view STAIRCASE
- view tiện ích điển hình: toilet, utility
- dải hẹp của partial plan (1/2, 2/2)

Mặt cắt và mặt đứng: dim trục theo G1–G4, kèm dim cao độ.

## 3. Dim cao độ (Level dims) — mặt cắt / mặt đứng

- Gồm chuỗi giữa các level đang hiện và dim tổng (level thấp nhất → cao nhất).
- Đặt cạnh đầu level, phía trong 2D extent.
- Tool: `level_dims_add`.

## 4. Dim cửa sổ, cửa đi (Opening dims)

Đã tách ra file riêng: **`drafting-opening-dims.md`**. Gồm 5 quy tắc Q1–Q5: đủ dim đứng và dim ngang; host level → bệ → đỉnh; trục → mép → mép → trục; dim sát cửa; chỉ tham chiếu vào cửa.
