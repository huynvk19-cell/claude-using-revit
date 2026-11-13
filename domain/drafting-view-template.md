---
name: drafting-view-template
description: "Chuẩn gán view template theo loại bản vẽ khi khởi tạo bộ hồ sơ, cho mọi loại công trình: mỗi `Loại` của sổ + loại view Revit → khoá template trong profile, thứ tự tra khoá (theo tỷ lệ, khoá thay thế), view đã có template khác, tỷ lệ bị template khoá, dependent view; quy tắc VT1–VT10; lệnh `view_templates_apply`. View template per drawing kind."
metadata:
  updated: "2026-11-13"
  related: ["drafting-view-create", "drafting-coverage", "drafting-sheet-layout", "drafting-views-sheets"]
---

# Gán view template theo loại bản vẽ (View template per drawing kind)

Skill: `drafting-view-template`.
- **Đầu vào**: sổ bộ hồ sơ (`drafting-sheet-layout.md` §11), các dòng đã có `View id` (trạng thái `view` trở đi, kể cả view có sẵn từ trước).
- **Đầu ra**: mọi view của sổ nằm dưới đúng template của loại nó; cột `Template` của sổ ghi tên template thật.
- Chuẩn này chỉ nói **view nào dùng template nào**. Template ẩn / hiện gì là việc của `drafting-views-sheets.md` §4.

## 1. Loại bản vẽ → khoá template

- `Loại` = khoá ở cột `Loại` của sổ (`drafting-coverage.md` §2). Bảng này dùng chung cho **mọi loại công trình**: file `drafting-sheet-set-<loại>.md` nào cũng ghi `Loại` cho từng dòng.
- **Khoá** = tên dòng trong profile; profile ghi tên template thật của dự án cho mỗi khoá.
- Một loại có nhiều loại view thì mỗi loại view một khoá (template mặt bằng không gán được cho mặt cắt).

| Loại | View của bản vẽ | Khoá | Thiếu thì dùng khoá |
|---|---|---|---|
| `cover` | không có view (phối cảnh nếu có: 3D view) | `cover` | bỏ qua (`n/a`) |
| `sheet-list` | Schedule | — | — |
| `notes` | Legend · Drafting view | — · `legend` | — · hỏi |
| `schedule` | Schedule | — | — |
| `site` | Floor plan · mặt bằng định vị | `site` · `site-setout` | hỏi · `site` |
| `plan` | Floor plan | `plan` | hỏi |
| `plan-dims` | Floor plan | `plan-dims` | `plan` |
| `plan-finish` | Floor plan | `plan-finish` | hỏi |
| `ceiling` | Ceiling plan | `ceiling` | hỏi |
| `roof` | Floor plan | `roof` | `plan` |
| `elevation` | Elevation | `elevation` | hỏi |
| `section` | Section | `section` | hỏi |
| `stair` | Callout plan · Section · Detail (phóng lớn mặt cắt, mũi bậc) | `stair` · `stair-section` · `stair-detail` | hỏi · `section` · `detail` |
| `vertical` | Callout plan · Section | `vertical` · `vertical-section` | `stair` · `stair-section` |
| `enlarged` | Callout plan (mặt bằng kích thước) · mặt bằng lát sàn · Interior elevation | `enlarged` · `enlarged-finish` · `enlarged-elevation` | `toilet` · `enlarged` · hỏi |
| `door` | Schedule · Legend | — | — |
| `facade` | Elevation · Section / detail | `facade` · `detail` | `elevation` · hỏi |
| `detail` | Detail view · Drafting view | `detail` · `detail-drafting` | hỏi · `detail` |
| `thematic` | Floor plan, mỗi chuyên đề một khoá | `thematic-<chuyên đề>` | hỏi (không dùng `plan`) |
| `subset` | theo loại của từng view (`plan`, `elevation`, `section`, `detail`) | khoá của loại đó | như loại đó |

- Dấu `—`: loại view đó không gán template ở đây. Legend của Revit không nhận view template. Schedule theo schedule mẫu của dự án (`drafting-view-create.md` §1).
- Mặt bằng theo vùng (dependent view) không có dòng riêng: xem VT5.

## 2. Theo loại công trình

Bảng §1 không đổi theo loại công trình. Mỗi loại chỉ thêm các lưu ý sau:

| Loại công trình | Lưu ý |
|---|---|
| Nhà liên kế, biệt thự | Các **mẫu nhà** dùng chung một bộ template: mẫu A và mẫu B cùng `Loại` thì cùng template (VT6). |
| Nhà cao tầng | Hầm, khối đế, tầng điển hình đều là `plan`. Bản tổng mỗi tầng ở tỷ lệ nhỏ lấy khoá theo tỷ lệ (`plan@200`, VT2). Mặt bằng căn hộ là `enlarged`. |
| Sân bay | Mặt bằng nền phối hợp (BHS, wayfinding, an ninh) là `thematic`, mỗi chuyên đề một template riêng; không dùng template của mặt bằng kiến trúc chính. |
| Công cộng (trường học, bệnh viện, TTTM) | Mặt bằng thoát nạn / PCCC, phân luồng là `thematic`. Phòng điển hình là `enlarged`. |
| Công nghiệp | Mặt bằng xưởng thường 1:200 trở lên: khoá theo tỷ lệ (VT2). Mặt bằng sàn công nghiệp là `plan-finish`. |

## 3. Quy tắc

### VT1. Mỗi view của sổ có đúng một template

Mỗi view của sổ nằm dưới template mà profile ghi cho khoá của nó (§1). Ngoại lệ duy nhất: loại view mang dấu `—`.

### VT2. Thứ tự tra khoá

Với một view có khoá `k` và tỷ lệ 1:`n`, lấy tên đầu tiên có trong profile:
1. `k@n` (template riêng cho tỷ lệ đó, vd. `plan@200`);
2. `k`;
3. khoá ở cột "Thiếu thì dùng khoá" (cũng thử `@n` trước);
4. không có → **hỏi user một lần** cho mọi khoá còn thiếu, rồi ghi câu trả lời vào profile.

Không đoán tên template theo tên gần giống trong model.

### VT3. Chỉ gán, không tạo và không sửa template

Không tạo, duplicate, đổi tên hay sửa nội dung template. Template thiếu trong model → hỏi user (họ tạo, hoặc chỉ template khác).

### VT4. Template phải hợp loại view

Revit chỉ nhận template cùng họ view (mặt bằng sàn; mặt bằng trần; mặt đứng / mặt cắt / detail; drafting; 3D). Lệnh trả `not valid for this view` → không gán, xem lại khoá của dòng đó ở §1.

### VT5. Dependent view theo view cha

View cha và các dependent dùng chung một template. Gán ở view cha theo `Loại` của bản vẽ; không gán riêng cho từng vùng.

### VT6. Cùng loại thì cùng template (ngay hàng thẳng lối)

Các view cùng `Loại`, cùng loại view, cùng tỷ lệ trong một bộ hồ sơ dùng **một** template, để các tờ cùng loại thể hiện giống nhau (`drafting-work-rules.md` §2a). Chỉ lệch khi profile ghi rõ (vd. khoá riêng cho một hạng mục).

### VT7. View đã có template khác

- View chưa có template → gán.
- View đã có đúng template → giữ.
- View đang ở template khác (view có sẵn, view dùng lại) → **không tự đổi**. Lập bảng `view · đang có · cần có` và hỏi user một lần. User đồng ý cho view nào thì gán view đó với `replace:true`.

Đổi template làm đổi cách thể hiện của view (và bỏ các override riêng mà template quản). Vì vậy luôn chạy preview trước và giữ log để undo.

### VT8. Tỷ lệ của sổ thắng

Template khoá tỷ lệ khác cột `Tỷ lệ` của sổ → không gán, báo user (VC4). Hướng xử lý: dùng khoá theo tỷ lệ (VT2), hoặc user bỏ khoá tỷ lệ trong template. Không sửa template, không đổi tỷ lệ của sổ.

### VT9. Gán trước khi chỉnh crop và đặt view

Template quyết định view hiện gì và (khi khoá) tỷ lệ, nên quyết định kích thước view trên giấy. Gán template xong mới chỉnh crop, đầu trục và đặt view lên sheet. Đổi template của một view đã đặt → kiểm lại vị trí của sheet đó.

### VT10. Kiểm sau khi gán

Chạy lại preview: mọi view của sổ là `Ok`, không còn dòng `Problems` nào chưa có lý do. Ghi tên template thật vào cột `Template` của sổ. View mang dấu `—` ghi `—`. View chưa gán được ghi `!` kèm lý do.

## 4. Lệnh

**`view_templates_apply`** (chi tiết ở phần `// ---- Details` của `revit-commands/view_templates_apply.cs`):
- `mode:"list"`: (chỉ đọc) template của model: `Name`, `Type` (loại view), `Scale` (1:n nếu template khoá tỷ lệ, 0 nếu không), `Used` (số view đang dùng). Có `items` thì chỉ trả các template được nêu tên, kèm `NotFound`.
- `items:[{template, viewIds, scale}]`: mỗi template một item; `scale` = tỷ lệ của sổ (VT8).
- `mode:"preview"` → `apply` (cần `logPath`) → `undo`. `replace:true` mới đổi view đang có template khác (VT7).
- Kết quả: `Views`, `Ok`, `Set`, `Replaced`, `Problems [{Id, Name, Has, Want, Why}]`.

Đọc template hiện có của các view trên sheet: `sheet_views_ids`.
