---
name: drafting-annotation
description: "Chuẩn đặt tag cửa đi/cửa sổ trên mặt đứng-mặt cắt, room tag, view title trên sheet. Tag, room tag and viewport title standards."
metadata:
  updated: "2026-10-05"
  related: ["drafting-work-rules", "drafting-views-sheets", "drafting-tools"]
---

# Chuẩn ghi chú (Annotation standards)

## 1. Tag cửa đi / cửa sổ — mặt đứng, mặt cắt

**Vị trí mặc định**
- Đặt **ngay phía trên** cửa của nó, căn giữa, sát cửa.
- Leader đứng ngắn đi vào cửa, đầu leader cách mép ~1.5 mm giấy.
- Nét đứt của level/grid được phép đi qua tag.

**Cửa sổ xếp chồng**
- Tag của cửa trên đặt phía trên cửa trên.
- Không đặt 2 tag vào cùng một khe.

**Cửa đi bị vướng phía trên** (dầm, poche sàn, dim)
- Nâng tag lên mảng tường sạch, tối đa ~10 mm giấy, leader dài hơn.
- Vẫn vướng → đặt tag **bên cạnh** cửa, ngang đỉnh cửa, leader ngang.

**Cửa cuốn**
- Đầu leader nằm ~2.5 mm bên trong **đỉnh nhìn thấy** của cửa.
- Hộp cuốn phía sau tường không tính là đỉnh cửa.

**Chồng lấn: được và không được**
- Chấp nhận: dầm link đè lên hàng cửa sổ; room tag chồng nhau trong mặt cắt.
- Không chấp nhận: dầm đè lên tag cửa đi.

**Căn hàng và chỉnh tay**
- Các tag cùng hàng → cao độ đầu tag bằng nhau (`tag_align`).
- **Không** chạy lại "đặt lại toàn bộ" trên view mà user đã chỉnh tay.

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
