---
name: drafting-annotation
description: "Chuẩn đặt tag cửa đi/cửa sổ trên mặt đứng-mặt cắt, room tag, view title trên sheet. Tag, room tag and viewport title standards."
metadata:
  updated: "2026-10-05"
  related: ["drafting-work-rules", "drafting-views-sheets", "drafting-tools"]
---

# Chuẩn ghi chú (Annotation standards)

## 1. Tag cửa đi / cửa sổ — mặt đứng, mặt cắt

Đã tách ra file riêng **`drafting-opening-tags.md`** (quy tắc T1–T8) và skill `drafting-opening-tags`.

## 2. Room tag

| Tình huống | Xử lý |
|---|---|
| Tag nằm ngoài phòng của nó | Dời về location point của room, tắt leader (`room_tags_outside`). |
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
