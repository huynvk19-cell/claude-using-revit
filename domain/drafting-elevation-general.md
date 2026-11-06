---
name: drafting-elevation-general
description: "Chuẩn khai triển mặt đứng chung, mặt cắt chung (thường tỉ lệ 1/200). Hiện có: fill kết cấu bị cắt (SF1–SF6). General elevation and section standard."
metadata:
  updated: "2026-11-06"
  related: ["drafting-opening-dims", "drafting-opening-tags", "drafting-views-sheets", "drafting-work-rules"]
---

# Khai triển mặt đứng chung (General elevations and sections)

**Trạng thái: đã có thẻ đầu tiên.** Mỗi quy tắc mới được thêm thành một mục khi đã chốt với user.

Áp dụng cho mặt đứng và mặt cắt chung của công trình đặt trên sheet, thường tỉ lệ **1/200**. Không áp dụng cho mặt cắt thang (`drafting-stair-section-parallel.md`, `drafting-stair-section-cross.md`).

Skill: `drafting-elevation-general`; mỗi nhóm quy tắc dưới đây là một thẻ bước của skill.

## Fill kết cấu bị cắt (thẻ 00, user 2026-11-06)

| Mã | Quy tắc |
|---|---|
| SF1 | Tô filled region theo **mặt cắt thật** của cấu kiện bị mặt phẳng cắt của view cắt qua, trong crop. Chỉ gồm Structural Framing, Floors kết cấu và Structural Foundations của model kết cấu. |
| SF2 | **Không tô sàn hoàn thiện**, sàn site (đường, vỉa hè, đất): chúng không phải kết cấu. |
| SF3 | **Bê tông và thép là hai vùng riêng**, mỗi loại một type theo Structural Material. Không gộp, không hỏi lại. Móng không có vật liệu trong model: tính là bê tông và nêu trong báo cáo. |
| SF4 | Các cấu kiện cùng type **chạm nhau thì hoà thành một vùng**, không có nét giữa (dầm + sàn, sàn + móng, bản thép + thép góc). Cấu kiện không chạm nhau trong model thì giữ riêng. |
| SF5 | Nét biên vùng **đậm số 8**. Tên line style lấy từ profile. |
| SF6 | Vùng mới dùng type đánh dấu (R10). Type đánh dấu giữ nguyên hatch user đã sửa khi chạy lại. Không đụng filled region type khác và vùng của người khác. |

Lệnh: `structural_cut_fill` (mỗi lần một view).
