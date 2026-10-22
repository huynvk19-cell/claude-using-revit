---
name: drafting-ceiling-plan
description: "Chuẩn khai triển mặt bằng trần (reflected ceiling plan): các bước và quy tắc riêng của mặt bằng trần. Hiện có: tag room (TR1–TR8). Reflected ceiling plan standard."
metadata:
  updated: "2026-10-15"
  related: ["drafting-annotation", "drafting-grid-dims", "drafting-work-rules", "drafting-tools"]
---

# Khai triển mặt bằng trần (Reflected ceiling plan)

Áp dụng cho các view mặt bằng trần (Ceiling Plan) đặt trên sheet.

Skill: `drafting-ceiling-plan`; mỗi mục dưới đây là một thẻ bước của skill.

| Thẻ | Mục | Quy tắc |
|---|---|---|
| 00 | Tag room | `drafting-annotation.md` §2 TR1–TR8: leader ngắn nhất, trong room nếu đủ chỗ (leader 0 = điểm tối đa, giảm về 0 khi leader dài ra), thẳng hàng +2; nằm trên tường / cạnh trần, leader trùng leader / trùng tường / qua chấm leader khác → 0; leader qua room khác −3 mỗi room; thiếu tag → điểm thẻ 0. |

Dim trục: `drafting-grid-dims.md` (G1–G4), chạy trước trong quy trình `ceiling-plan`.

Các mục khác của mặt bằng trần (cao độ trần, tag trần, đèn, dim trần…) sẽ thêm thành thẻ mới khi có quy tắc.
