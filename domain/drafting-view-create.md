---
name: drafting-view-create
description: "Chuẩn tạo view cho sheet từ sổ bộ hồ sơ: mỗi loại bản vẽ → loại view Revit (floor plan, ceiling plan, duplicate, dependent, elevation, section, callout, detail, drafting, legend, schedule), tạo từ đâu, level, template, tỷ lệ, crop, tên view; quy tắc VC1–VC12; lệnh có / chưa có. View creation standard."
metadata:
  updated: "2026-11-13"
  related: ["drafting-sheet-layout", "drafting-sheet-create", "drafting-view-template", "drafting-views-sheets", "drafting-tools"]
---

# Tạo view cho sheet (View creation)

Skill: `drafting-view-create`.
- **Đầu vào**: sổ bộ hồ sơ (`drafting-sheet-layout.md` §11), các dòng ở trạng thái `sheet` mà cột `Có sẵn` = `chưa có view`.
- **Đầu ra**: cột `Loại view`, `Tên view`, `View id`, `Template`, `Crop`, và trạng thái `view`.
- Dòng `ok` / `chưa đặt` đã có view: chỉ kiểm theo VC2–VC5, không tạo lại.

## 1. Loại bản vẽ → view Revit

Tên template là **khoá trong profile**: profile ghi tên template thật của dự án cho mỗi khoá. Khoá chưa có tên trong profile → hỏi user. Bảng khoá đầy đủ theo `Loại` và loại view (mặt cắt thang, interior elevation, khoá theo tỷ lệ…): `drafting-view-template.md` §1, VT2.

| Bản vẽ | Loại view Revit | Tạo từ | Level / vị trí | Template (khoá profile) | Crop |
|---|---|---|---|---|---|
| Tờ bìa | không có view (khung tên của sheet); phối cảnh nếu có: 3D view hoặc ảnh | — | — | — | — |
| Danh mục bản vẽ | Sheet List schedule (đã có theo profile) | — | — | — | — |
| Ghi chú, ký hiệu chung | Legend (ký hiệu) + Drafting view (ghi chú chữ) | — | — | `legend` | — |
| Chi tiết cấu tạo sàn, trần, tường | Legend (Legend Component của type tường / sàn / trần) hoặc Drafting view | — | — | `detail` | — |
| Mặt bằng vị trí khu đất | Drafting view (nền quy hoạch), hoặc Floor plan site ở tỷ lệ nhỏ | — | site / trệt | `site` | khu đất + đường xung quanh |
| Mặt bằng định vị khu đất | Floor plan | mới | site / trệt | `site-setout` | lô đất + vỉa hè + mốc |
| Mặt bằng tổng thể | Floor plan | mới | site / trệt | `site` | lô / dãy nhà + đường |
| Mặt bằng sàn `<tầng>` | Floor plan | mới | level của tầng | `plan` | nhà + dải chú thích (dim trục, bubble) |
| Mặt bằng lát sàn `<tầng>` | Floor plan | **Duplicate** (không detailing) từ MB sàn cùng tầng | level của tầng | `plan-finish` | = MB sàn |
| Mặt bằng mái, sân thượng | Floor plan, view range thấy hết mái | mới | level sân thượng / mái | `roof` | = MB sàn |
| Mặt bằng trần `<tầng>` | Ceiling plan | mới | level của tầng | `ceiling` | = MB sàn |
| Mặt đứng `<hướng>` | Elevation (building elevation) | marker đặt trên MB trệt, ngoài nhà, nhìn vào mặt nhà | — | `elevation` | cos nền − 1 m → đỉnh mái + dải chú thích; far clip vừa qua mặt nhà |
| Mặt cắt `<tên>` | Section | đường cắt trên MB trệt: dọc qua thang + giếng trời, ngang qua phòng chính | — | `section` | đáy hầm / móng → đỉnh mái |
| Chi tiết thang bộ `<mã ST>`: mặt bằng, mặt cắt | Callout plan mỗi tầng + Section qua vế (dọc; ngang nếu cần) | callout từ MB sàn từng tầng; section trên callout tầng trệt | level từng tầng | `stair` · `stair-section` | lõi thang + tường + trục gần nhất |
| Chi tiết thang bộ `<mã ST>`: chi tiết phóng lớn mặt cắt (CT2) | Detail callout 1:20–1:10 | từ mặt cắt thang: chân thang, vế gặp chiếu nghỉ, bậc cuối gặp sàn | — | `stair-detail` | 3–5 bậc + chiếu nghỉ / sàn + lan can |
| Chi tiết thang bộ `<mã ST>`: chi tiết mũi bậc (CT2) | Detail callout 1:5–1:2; Drafting view khi là chi tiết chuẩn | từ chi tiết phóng lớn (hoặc mặt cắt thang) | — | `stair-detail` | 1–2 bậc |
| Chi tiết thang máy `<mã LF>` (CT3) | Callout plan mỗi tầng khác nhau + Section qua hố | callout từ MB sàn | level từng tầng | `vertical` · `vertical-section` | hố thang + cửa tầng; section từ đáy pit → nóc OH |
| Chi tiết vệ sinh `<mã WC>`: mặt bằng kích thước (CT1) | Callout plan 1:25 | callout từ MB sàn | level của phòng vệ sinh | `enlarged` | phòng vệ sinh + tường bao |
| Chi tiết vệ sinh `<mã WC>`: mặt bằng lát sàn (CT1) | Floor plan | **Duplicate** (không detailing) từ mặt bằng kích thước của cùng vệ sinh | như trên | `enlarged-finish` | = mặt bằng kích thước |
| Chi tiết vệ sinh `<mã WC>`: triển khai tường | Interior elevation từng tường có thiết bị / ốp lát | marker nội thất trong phòng vệ sinh, đặt trên mặt bằng kích thước | như trên | `enlarged-elevation` | tường + sàn + trần |
| Chi tiết tam cấp, bồn hoa, ramp, ban công, mái, mặt tiền, giếng trời, tiếp giáp, thu nước mưa | Detail view (detail section / callout) cắt qua model; Drafting view khi là chi tiết chuẩn không lấy từ model | từ MB / mặt cắt / mặt đứng có liên quan | — | `detail` | vùng chi tiết |
| Bảng thống kê cửa đi, cửa sổ | Schedule (Doors, Windows) | Duplicate schedule mẫu của dự án | — | (schedule mẫu) | — |
| Chi tiết cửa đi, cửa sổ | Legend có Legend Component từng type cửa (Elevation) | — | — | `legend` | — |
| Key plan (công trình chia vùng) | Legend hoặc Drafting view dùng chung | — | — | `legend` | — |
| Mặt bằng theo vùng | Dependent view của view cha, mỗi scope box một dependent | **Duplicate as Dependent** | như view cha | như view cha | scope box của vùng |

## 2. Quy tắc

| Mã | Quy tắc |
|---|---|
| VC1 | **Chỉ tạo view có trong sổ.** Trước khi tạo, tìm view trùng (cùng loại, cùng level, cùng vùng, chưa đặt lên sheet). Có view trùng thì dùng lại view đó và ghi id vào sổ, không tạo bản thứ hai. |
| VC2 | **Tên view đầy đủ** theo TG1–TG6 (`drafting-sheet-layout.md` §8), mẫu của profile chỉ thêm vào chứ không rút gọn. Mặc định = tên bản vẽ trong sổ, in hoa, kèm vùng (`MẶT BẰNG SÀN LẦU 1 - VÙNG A`). View con của cùng một bản vẽ thêm ` - <nội dung view>` viết đủ chữ (`CHI TIẾT THANG BỘ ST1 - MẶT BẰNG TẦNG TRỆT`, `CHI TIẾT VỆ SINH WC1 - MẶT BẰNG LÁT SÀN`); không viết tắt (`MB`, `MC`, `WC`). Thành phần cùng loại giữ mã TG6 (`ST1`, `WC1`…) trong mọi view con. Tên phải duy nhất trong model. Không đưa số sheet vào tên view. |
| VC3 | **Mỗi view có một view template** theo bảng §1. Template thiếu → hỏi user. Không tự tạo template, không để view không có template. |
| VC4 | **Tỷ lệ** = cột `Tỷ lệ` của sổ. Template nào khoá tỷ lệ thì tỷ lệ đó phải trùng với sổ; lệch → báo user, không sửa template. |
| VC5 | **Crop bật**, annotation crop bật. Crop ôm phần cần thể hiện cộng dải chú thích. Mặt bằng các tầng cùng loại dùng **cùng một crop** (cùng scope box nếu profile có), để đặt cùng vị trí trên sheet (LO4). |
| VC6 | **Mặt bằng phụ cùng tầng** (lát sàn…): Duplicate *không detailing* từ MB sàn của tầng. Duplicate with Detailing chỉ khi user yêu cầu. Chú thích của loại nào nằm ở view của loại đó. |
| VC7 | **Mặt đứng / mặt cắt**: marker hoặc đường cắt đặt trên MB trệt (hoặc view theo profile), đúng hướng nhìn, far clip vừa đủ. Mặt đứng gọi theo hướng hoặc trục theo profile (`MẶT ĐỨNG CHÍNH`, `MẶT ĐỨNG TRỤC 1-5`); mặt cắt gọi `MẶT CẮT A-A`, `B-B`… theo thứ tự. |
| VC8 | **Callout / detail** tạo từ view gốc cùng loại (MB → callout plan; mặt cắt → detail callout), ở tỷ lệ chi tiết. Ký hiệu callout trên view gốc là chú thích hợp lệ (tự trỏ số sheet sau khi đặt, LO12). |
| VC9 | **Chia vùng**: mỗi scope box một dependent view (Z1), tên kèm vùng. View cha không đặt lên sheet. Bản tổng mỗi tầng (Z4) là một view riêng ở tỷ lệ nhỏ. |
| VC10 | **Phân loại view** (phase, discipline, tham số nhóm Project Browser) theo profile. View mới phải nằm đúng nhóm trong Project Browser. |
| VC11 | **Không sửa model để có view** (R1). Không xoá view của người khác. View tạo thử phải xoá ngay trong cùng lượt và ghi log. |
| VC12 | **Kiểm sau khi tạo**: mọi dòng của sổ có `View id`, tên theo VC2 (không còn tên viết tắt; thành phần cùng loại có mã TG6 trùng với tên sheet), chi tiết vệ sinh / thang bộ / thang máy đủ view con theo CT1–CT3 (`drafting-sheet-layout.md` §4b), đúng template, đúng tỷ lệ, crop bật; view chưa nằm trên sheet nào (đặt view là việc của `drafting-sheet-place`). |

## 3. Lệnh

**Đọc và chỉnh**
| Việc | Lệnh |
|---|---|
| Đọc view, level, template | `query_elements_with_filter` (category `Views`, `Levels`); `sheet_browser_names` |
| Đọc crop, scope box, tỷ lệ | `view_crop_info` |
| Kéo cạnh crop tới trục | `crop_side_to_grids` |

**Lệnh `view_create`** (tạo view; chi tiết đầy đủ ở phần `// ---- Details` của `revit-commands/view_create.cs`):
- `items[]`, mỗi item gồm:
  - `kind`: `floorPlan` | `ceilingPlan` | `duplicate` | `dependent` | `elevation` | `section` | `callout` | `detail` | `drafting` | `legendCopy` | `scheduleCopy`;
  - `name`, `level`, `sourceViewId`, `scopeBox`, `template`, `scale`, `viewType`, `params {tên tham số: giá trị}` (nhóm Project Browser, VC10);
  - `marker {x, y, direction, depth, width, bottom, top}` (mặt đứng; `direction` = hướng **nhìn**: `east` | `north` | `west` | `south`, số độ hoặc `{x, y}`);
  - `line {start, end, depth, bottom, top}` (mặt cắt; view nhìn về **bên trái** của hướng start → end);
  - `box {min, max}` (callout, detail). Toạ độ mm model; `bottom` / `top` là mm so với gốc nội bộ.
- `mode`: `preview` | `apply` | `undo`; `logPath` (bắt buộc với apply, undo).
- Kết quả: `Ids` theo thứ tự item, `Problems` gồm `Error`, `Warn`, `Reused` (view cùng tên cùng loại đã có: dùng lại, VC1), `Similar` (mặt bằng cùng level chưa đặt lên sheet: xem trước khi tạo cái thứ hai).
- Tự làm: tên (VC2), template (VC3: template nêu tên mà không có trong model → lỗi, không tạo view), tỷ lệ (VC4: lệch do template → `Warn`), scope box, bật crop + annotation crop (VC5).
- Không làm được: tạo legend mới (Revit API không cho; chỉ copy legend đã có); crop theo trục thì dùng `crop_side_to_grids` sau khi tạo.

Item báo `Error` mà không sửa được từ profile: đưa đúng dòng đó cho user tạo tay (loại, tạo từ, level, template, tỷ lệ, tên), rồi đọc lại bằng các lệnh ở trên và kiểm theo VC12.
