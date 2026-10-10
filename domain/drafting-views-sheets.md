---
name: drafting-views-sheets
description: "Chuẩn view/sheet: viewport type, crop và scope box, dependent view, đầu trục 2D, view template, hiển thị link, danh mục bản vẽ + print set. Views, sheets, crops, templates, links, drawing list, print set."
metadata:
  updated: "2026-10-10"
  related: ["drafting-work-rules", "drafting-annotation", "drafting-tools", "drafting-api-pitfalls"]
---

# View và sheet (Views and sheets)

## 1. Viewport

**Gọi tên view theo Project Browser**:

```text
CLASSIFICATION › SHEET NUMBER - SHEET NAME › <View type>: <view name>
```

Tool tra cứu:
- `sheet_browser_names`: tên sheet/view như trong Project Browser.
- `sheet_views_ids`: id của view và viewport.

Thao tác:
- Đổi viewport type: `set_viewport_type`. Có undo từ log.
- Dời viewport theo comment: ghi lại khoảng dời. Sau đó căn lại title.

## 2. Crop và scope box

- Crop là view setting nên được phép sửa. Tool: `crop_side_to_grids`, `view_crop_info`.
- **View gắn Scope Box thì crop bị khoá**:
  - Sửa crop qua API không có tác dụng.
  - Ghi vào việc tồn để chỉnh scope box bằng tay.
- Dependent view:
  - Annotation thuộc view cha.
  - Annotation hiện ở mọi dependent có crop chứa nó.
  - Muốn chỉ hiện ở một dependent → ẩn trong các dependent còn lại.

## 3. Đầu trục / level (Grid and level ends)

- Chỉ sửa **2D extent** (ViewSpecific). **Không** đổi 3D extent.
- Căn đầu trục thẳng hàng: `align_grid_ends`.
- Kéo đầu trục ra: chỉ làm khi user yêu cầu. Dim trục kéo theo.
- Bubble chồng nhau:
  - Ưu tiên **so le đầu trục**: dời một đầu 2D.
  - `grid_bubble_elbow` hay báo lỗi "leader not valid".

## 4. View template và Visibility/Graphics

**Ẩn/hiện**
- Sửa **View Template** đang điều khiển view.
- Báo cả các view khác dùng chung template đó.
- Muốn xem trước hiệu quả: `view_image_trial` (luôn rollback).

**Hiển thị link trên Revit 2023** (không có API)
- Mục cần chỉnh: V/G › Revit Links › Display Settings = Custom.
- Annotation Categories = `<Custom>`, bỏ tick "Show annotation categories in this view".
- Chỉ làm bằng cách điều khiển UI, và chỉ khi user yêu cầu.
- Xem link nào đang hiện trong template: `template_link_visibility`.

## 5. Danh mục bản vẽ và print set (Drawing list, print set)

Khi thêm, đổi tên hoặc đổi số một sheet (user, 2026-10-10):
1. Kiểm sheet có trong schedule danh mục bản vẽ (tên schedule ở profile; sheet phải có Appears In Sheet List = Yes).
2. So print set của dự án với schedule: `print_set_info {setName, scheduleName}`.
3. Thiếu / thừa sheet → `print_set_from_schedule {mode:"apply", scheduleName, setName, replace:true}`.
4. Ghi vào log và báo user: số sheet trước / sau, sheet được thêm / bỏ.

Tên print set và tên schedule lấy từ `drafting-profile.md`. Không tự đổi tên print set nếu user chưa yêu cầu.
