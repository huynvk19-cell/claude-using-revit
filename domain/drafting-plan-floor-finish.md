---
name: drafting-plan-floor-finish
description: "Chuẩn khai triển mặt bằng hoàn thiện sàn (floor finish plan): mã hoàn thiện sàn trên mặt bằng các tầng. Hiện có: tag cao độ sàn (FL1–FL3). Floor finish plan standard."
metadata:
  updated: "2026-10-16"
  related: ["drafting-plan-dims", "drafting-stair-plan", "drafting-annotation", "drafting-work-rules"]
---

# Khai triển mặt bằng hoàn thiện sàn (Floor finish plan)

**Trạng thái: đã có thẻ đầu tiên.** Mỗi quy tắc mới được thêm thành một mục trong bảng dưới khi đã chốt với user.

Áp dụng cho mặt bằng các tầng đặt trên sheet, cùng phạm vi với `drafting-plan-dims.md`. Không áp dụng cho mặt bằng lõi thang (sàn lõi thang là SB5 trong `drafting-stair-plan.md`) và mặt bằng trần (`drafting-ceiling-plan.md`).

Skill: `drafting-plan-floor-finish`; mỗi mục dưới đây là một thẻ bước của skill.

| Thẻ | Mục | Quy tắc |
|---|---|---|
| 00 | Tag cao độ sàn | `drafting-annotation.md` §2b FL1–FL3: mỗi gian phòng một cao độ (trừ phòng nhỏ nằm trong phòng khác); buồng thang có cao độ bên trong và bên ngoài cửa; type theo người dùng. Cùng thẻ với `drafting-plan-dims` thẻ 03. |

## Điểm cần chốt với user

- Mã hoàn thiện sàn (vd. `F13`) lấy từ đâu: tham số hoàn thiện của Room, bảng hoàn thiện trên sheet, hay hỏi user. Không suy ra được thì hỏi, không tự đặt mã.
- Loại tag hoàn thiện: Generic Annotation (ô chữ, như `F..` của lõi thang) hay IndependentTag. Chưa thống nhất → hỏi, ghi vào `drafting-profile.md`.
- Vị trí đặt mã: một mã mỗi loại hoàn thiện trên mỗi view, hay mỗi phòng. Chưa chốt.

## Ghi chú

- Dự án đã thống nhất gì thì ghi vào `drafting-profile.md`, không ghi vào file này.
