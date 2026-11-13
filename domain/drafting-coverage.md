---
name: drafting-coverage
description: "Ma trận độ phủ: mỗi loại bản vẽ của bộ hồ sơ (khoá ở cột Loại) → skill, workflow, lệnh làm, lệnh kiểm và mức Claude làm được (1–4); chuỗi khâu chung từ sổ bộ hồ sơ tới PDF. Coverage matrix: drawing kind → skill, commands, level."
metadata:
  updated: "2026-11-13"
  related: ["drafting-sheet-layout", "drafting-view-create", "drafting-view-template", "drafting-sheet-place"]
---

# Ma trận độ phủ (Coverage matrix)

Trả lời một câu hỏi: **với mỗi loại bản vẽ, Claude làm được tới đâu mà không cần người làm tay?**

- Mỗi dòng bản vẽ trong `drafting-sheet-layout.md` §4 và trong các file `drafting-sheet-set-<loại>.md` có cột **`Loại`**: khoá của bảng §2 dưới đây. Một dòng gồm nhiều loại ghi nhiều khoá (`stair`, `vertical`): trong sổ bộ hồ sơ mỗi loại là một dòng con.
- Sổ bộ hồ sơ chép `Loại` và `Mức` cho từng dòng (`drafting-sheet-layout` thẻ 01), để ngay từ đầu user biết sheet nào Claude làm trọn, sheet nào làm một phần, sheet nào phải làm tay.
- Khi triển khai một dòng của sổ: tra `Loại` ở §2 để lấy đúng workflow và skill. Không đoán, không đọc file khác để tìm.

## 1. Thang mức

| Mức | Nghĩa | Claude nói với user |
|---|---|---|
| 1 | Chỉ có tên trong bộ hồ sơ và cách tạo view (`drafting-view-create.md` §1). Chưa có chuẩn triển khai nội dung. | "làm tay": Claude lập sheet, title, danh mục; nội dung do user làm. Ghi vào Việc tồn. |
| 2 | Có skill, làm được một phần bằng thẻ bước; còn thiếu nội dung (cột Còn thiếu). | "một phần": nói rõ phần nào làm, phần nào không. |
| 3 | Có lệnh làm một phát cho phần đã có chuẩn. | "một phần" nếu cột Còn thiếu còn mục; không thì "trọn". |
| 4 | Lệnh làm + lệnh kiểm + workflow: trọn vòng, chấm điểm được. | "trọn". |

Mức là của **nội dung trong view**. Các khâu quanh nó (tạo sheet, tạo view, đặt view, title, danh mục) xem §3.

## 2. Loại bản vẽ → skill, lệnh, mức

| Loại | Bản vẽ | Skill | Workflow | Lệnh làm | Lệnh kiểm | Mức | Còn thiếu |
|---|---|---|---|---|---|---|---|
| `cover` | Tờ bìa | — | — | — | — | 1 | chuẩn nội dung bìa |
| `sheet-list` | Danh mục bản vẽ, print set | `drafting-views-sheets` | `sheet-layout` | `cur_task` | `print_set_info` | 4 | — |
| `notes` | Ghi chú chung, ký hiệu, legend, key plan, sơ đồ chia vùng | — | — | — | — | 1 | skill + lệnh cho legend và drafting view (nội dung bảng ký hiệu + vật liệu gộp của nhà liên kế / biệt thự đã có chuẩn: `drafting-sheet-layout.md` §8b) |
| `schedule` | Bảng chỉ tiêu, bảng vật liệu, bảng phân loại căn | — | — | — | — | 1 | skill (task đọc / ghi schedule đã có: `cur_task`) |
| `site` | Tổng mặt bằng, vị trí, định vị, sân vườn, tổ hợp dãy | — | — | — | — | 1 | chuẩn: chỉ giới, khoảng lùi, toạ độ mốc, cao độ nền |
| `plan` | Mặt bằng tầng (cả hầm, khối đế, tầng điển hình, theo vùng) | `drafting-grid-dims`, `drafting-tags-titles` | `floor-plan` | `view_develop`, `grid_dims_band` | `grid_dims_audit` | 4 | — |
| `plan-dims` | Mặt bằng kích thước (khai triển) | `drafting-plan-dims` | `floor-plan` | `view_develop` | `door_window_tag_check`, `room_tags_score` | 4 | một lệnh kiểm gộp cho cả view |
| `plan-finish` | Mặt bằng hoàn thiện sàn, lát sàn, sàn công nghiệp | `drafting-plan-floor-finish` | `floor-plan` | `view_develop`, `finish_mark_place` | `finish_marks_survey` | 3 | thẻ cho mã hoàn thiện, lát sàn, khe co giãn (mới có cao độ + độ dốc) |
| `ceiling` | Mặt bằng trần | `drafting-ceiling-plan` | `ceiling-plan` | `view_develop` | `room_tags_score` | 2 | cao độ trần, vật liệu, giật cấp, đèn, cửa thăm (mới có dim trục + tag room) |
| `roof` | Mặt bằng mái, sân thượng | `drafting-grid-dims`, `drafting-plan-dims` | — | `slope_spots_place` | — | 2 | skill riêng: thoát nước, sê nô, ống đứng, phễu thu (mới có dim trục + ký hiệu độ dốc) |
| `elevation` | Mặt đứng | `drafting-grid-dims`, `drafting-dims`, `drafting-opening-dims`, `drafting-opening-tags` | `elevation-section` | `view_develop`, `elevation_opening_dims`, `elevation_opening_tags` | `grid_dims_audit`, `door_window_tag_check` | 4 | — |
| `section` | Mặt cắt | `drafting-section-general`, `drafting-grid-dims`, `drafting-dims`, `drafting-opening-dims`, `drafting-opening-tags` | `elevation-section` | `view_develop`, `structural_cut_fill` | `grid_dims_audit`, `door_window_tag_check` | 4 | — |
| `stair` | Chi tiết thang bộ: mặt bằng lõi + mặt cắt dọc / ngang vế + chi tiết phóng lớn mặt cắt + chi tiết mũi bậc | `drafting-stair-plan`, `drafting-stair-section-parallel`, `drafting-stair-section-cross` | `stair-core` | `view_develop`, `stair_plan_annotate`, `stair_section_annotate`, `stair_xsection_annotate` | `stair_plan_audit` | 4 | thang xoắn, thang cong; nội dung chi tiết phóng lớn mặt cắt và mũi bậc (CT2: mới lập view + sheet, hình cấu tạo làm tay) |
| `vertical` | Chi tiết thang máy, ram dốc, thang cuốn, băng chuyền người | — | — | — | — | 1 | skill + lệnh ghi kích thước hố, pit, OH (bộ view thang máy đã có chuẩn: `drafting-sheet-layout.md` CT3); chuẩn: độ dốc ram, cao độ đầu / cuối |
| `enlarged` | Phòng / khu phóng to + triển khai tường: chi tiết vệ sinh (mặt bằng kích thước + mặt bằng lát sàn), bếp, căn hộ, phòng điển hình, sảnh | — | — | — | — | 1 | skill + lệnh triển khai nội dung mặt bằng kích thước, mặt bằng lát sàn, interior elevation (bộ view của chi tiết vệ sinh đã có chuẩn: `drafting-sheet-layout.md` CT1); họ này phủ nhiều loại công trình nhất |
| `door` | Thống kê cửa, chi tiết / mặt đứng cửa, vách kính | — | — | — | — | 1 | skill (schedule cửa + legend từng mã cửa) |
| `facade` | Triển khai mặt dựng (module kính, lam) | — | — | — | — | 1 | chuẩn module, chia đố |
| `detail` | Chi tiết cấu tạo: mái, ban công, mặt tiền, khe lún, chống thấm… | — | — | — | — | 1 | thư viện chi tiết của văn phòng + chuẩn ghi chú; Claude không tự vẽ cấu tạo |
| `thematic` | Mặt bằng chuyên đề: phân luồng, thoát nạn / PCCC, an ninh, phân lô, biển chỉ dẫn, thiết bị | — | — | — | — | 1 | chuẩn tô màu theo filter + legend |
| `subset` | Bộ con của hạng mục phụ (cổng rào, nhà bảo vệ, hồ bơi…): mặt bằng + mặt đứng + mặt cắt | `drafting-grid-dims`, `drafting-dims` | — | — | — | 2 | mỗi view làm theo loại của nó (`plan`, `elevation`, `section`, `detail`); chưa có luật riêng cho hạng mục |

- Dòng mức 1: không tự nghĩ ra cách triển khai. Lập sheet và view theo §3, ghi nội dung vào Việc tồn.
- Dòng mức 2–3: làm phần skill có, cột **Còn thiếu** đi vào Việc tồn.

## 3. Chuỗi khâu chung (mọi loại bản vẽ)

| # | Khâu | Skill | Lệnh | Trạng thái |
|---|---|---|---|---|
| 1 | Lập sổ bộ hồ sơ theo loại công trình | `drafting-sheet-layout` | — (Claude lập file sổ) | tự động |
| 2 | Tạo sheet | `drafting-sheet-create` | `cur_task` | tự động |
| 3 | Tạo view | `drafting-view-create` | `view_create` | tự động; trừ legend đầu tiên của mỗi loại (Revit không cho tạo legend mới, chỉ copy) |
| 4 | Gán view template theo loại bản vẽ | `drafting-view-template` | `view_templates_apply` | tự động; template thiếu trong model thì user tạo (Claude không tạo, không sửa template) |
| 5 | Chỉnh view: crop, nội dung template, đầu trục | `drafting-views-sheets` | `crop_side_to_grids`, `align_grid_ends` | tự động |
| 6 | Triển khai nội dung | theo cột Loại (§2) | theo cột Loại (§2) | theo mức |
| 7 | Đặt view lên sheet | `drafting-sheet-place` | `viewport_place` | tự động; trừ mũi tên Bắc và ký hiệu khác (không phải view) |
| 8 | Title, danh mục, print set, PDF | `drafting-tags-titles`, `drafting-views-sheets` | `viewport_titles_place`, `viewport_title_audit`, `cur_task` | tự động |
| 9 | Kiểm bằng ảnh, kết thúc | `drafting-visual-check`, `drafting-end` | `export_sheet_images` | tự động |

Một lệnh của bảng này chưa viết thì ghi `(chưa có)` sau tên lệnh và ghi **làm tay** ở cột Trạng thái.

## 4. Cập nhật

- Thêm dòng vào một file bộ hồ sơ → điền cột `Loại` bằng khoá có ở §2. Loại bản vẽ chưa có khoá → thêm một dòng mức 1 vào §2.
- Thêm skill, lệnh hay workflow cho một loại → sửa dòng của nó ở §2 (và §3 nếu là lệnh của một khâu chung).
- Nâng mức chỉ khi đủ điều kiện: mức 2 có skill; mức 3 có lệnh làm; mức 4 có thêm lệnh kiểm và workflow.
- Kiểm: `python addin/tests/coverage_check.py` (tên skill / workflow / lệnh có thật, mức đủ điều kiện, mọi dòng bộ hồ sơ có `Loại`); nó in ma trận theo từng loại công trình. CI chạy ở mỗi lần push.
