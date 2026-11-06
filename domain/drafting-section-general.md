---
name: drafting-section-general
description: "Chuẩn khai triển mặt cắt chung (thường tỉ lệ 1/200). Hiện có: fill kết cấu bị cắt (SF1–SF9). General section standard."
metadata:
  updated: "2026-11-06"
  related: ["drafting-opening-dims", "drafting-opening-tags", "drafting-views-sheets", "drafting-work-rules"]
---

# Khai triển mặt cắt chung (General sections)

**Trạng thái: đã có thẻ đầu tiên.** Mỗi quy tắc mới được thêm thành một mục khi đã chốt với user.

Áp dụng cho mặt cắt chung của công trình đặt trên sheet, thường tỉ lệ **1/200**. Không áp dụng cho mặt cắt thang (`drafting-stair-section-parallel.md`, `drafting-stair-section-cross.md`).

Skill: `drafting-section-general`; mỗi nhóm quy tắc dưới đây là một thẻ bước của skill.

## Fill kết cấu bị cắt (thẻ 00, user 2026-11-06)

| Mã | Quy tắc |
|---|---|
| SF1 | **Quan trọng nhất: chỉ tô cấu kiện thật sự bị mặt phẳng cắt của view cắt qua**, trong crop, theo mặt cắt thật. Cấu kiện nhìn thấy phía sau, hoặc chỉ có mặt nằm đúng trên mặt phẳng cắt, không tô. Chỉ gồm Structural Framing, Floors kết cấu, Structural Foundations và Walls có tham số Structural bật của model kết cấu, theo SF7. |
| SF2 | **Không tô sàn hoàn thiện**, sàn site (đường, vỉa hè, đất): chúng không phải kết cấu. **Không tô móng đài cọc, cọc**, kể cả khi chạm sàn. |
| SF3 | **Bê tông và thép là hai vùng riêng**, mỗi loại một type theo Structural Material. Không gộp, không hỏi lại. Móng không có vật liệu trong model: tính là bê tông và nêu trong báo cáo. |
| SF4 | Các cấu kiện cùng type **chạm nhau thì hoà thành một vùng**, không có nét giữa (dầm + sàn, sàn + móng, bản thép + thép góc). Cấu kiện không chạm nhau trong model thì giữ riêng. |
| SF5 | Nét biên vùng **đậm số 8**. Tên line style lấy từ profile. |
| SF6 | Vùng mới dùng type đánh dấu (R10). Type đánh dấu giữ nguyên hatch user đã sửa khi chạy lại. Không đụng filled region type khác và vùng của người khác. |
| SF7 | **Dưới mặt đất** (đỉnh mặt cắt không cao hơn tầng trệt): chỉ tô sàn kết cấu dưới tầng trệt, hoặc sàn tầng cuối của tầng hầm. Cấu kiện dưới đất khác chỉ tô khi **kết nối** với các sàn đó, trực tiếp hoặc nối tiếp qua cấu kiện đã giữ (vd. sàn nền → tường kết cấu → dầm dưới tường). Khe không quá 50 mm vẫn tính là kết nối (khe co giãn, vd. tường 200 cách sàn nền 20 mm). Không kết nối thì bỏ. |
| SF9 | **Filled region đất** vẽ đè lên để **che** những gì phải ẩn dưới đất (cấu kiện không kết nối, đài cọc, cọc), không dùng template hay Hide in View. Lấy đường bao ngoài của vùng đất có sẵn trên view, bỏ các lỗ, cộng thêm hình các cấu kiện bị ẩn. Vùng đất phải che được (masking) và nằm dưới vùng kết cấu. Vùng đất của người khác giữ nguyên. |
| SF8 | **Mỗi view một filled region cho mỗi type**, để dễ quan sát: một vùng bê tông, một vùng thép, chứa mọi đường bao của loại đó. Riêng hình gộp mà Revit không vẽ được thì tách thành các vùng riêng và nêu trong báo cáo. |

Lệnh: `structural_cut_fill` (mỗi lần một view).
