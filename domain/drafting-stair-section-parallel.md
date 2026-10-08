---
name: drafting-stair-section-parallel
description: "Chuẩn triển khai MẶT CẮT thang bộ cắt SONG SONG với hướng đi (mặt cắt dọc vế thang): dim chiều cao vế có công thức (169.4mm x 16R = 2710 / EQUAL RISERS), dim cao độ tầng, dim chiều dài vế có công thức (280mm x 15T = 4200 / EQUAL TREADS), chiều cao thông thuỷ dưới chiếu nghỉ, chiều cao tay vịn, tường/cửa tới trục; tag vế, tay vịn, cao độ chiếu nghỉ, hoàn thiện sàn (F..) và trần/đáy chiếu nghỉ (C..), cửa; đánh số bậc liên tục. Stair section parallel to the stair path standard."
metadata:
  updated: "2026-10-08"
  related: ["drafting-stair-plan", "drafting-opening-tags", "drafting-dimensions", "drafting-tools"]
---

# Mặt cắt thang bộ song song hướng đi (Stair section parallel to the stair path)

Áp dụng cho **mặt cắt chi tiết thang bộ cắt dọc theo vế thang** (đường cắt song song stair path; view tên thường có STAIRCASE … SECTION, tỉ lệ 1:50).
Không áp dụng cho:
- **mặt bằng** lõi thang: `drafting-stair-plan`;
- **mặt cắt ngang vế** (đường cắt vuông góc stair path): chủ đề riêng, chưa viết.

Tool: `stair_section_info` (read-only), `level_dims_add` (cao độ tầng), `dims_text` (công thức, chữ dưới), `annotation_overlaps`. Skill: `drafting-stair-section-parallel`.

Mã quy tắc: **L** (mặt cắt dọc) — **LA** dim, **LB** tag, **LC** đếm/đánh số bậc. Không dùng lại SA–SD của mặt bằng.

Nguồn: ảnh mẫu của user (2026-10-07) + các quy tắc chung đã có ở `drafting-stair-plan` (số liệu từ model, công thức có giá trị đo sống, type lấy chung dự án).

## 0. Đọc mặt cắt trước khi làm

```text
   LA2                                                     LA1
 (cao độ tầng)                                     (chiều cao từng vế)
    |   F13 ▼7960 ______________            ______________ ▼7960 F13   |
    |   C09 ◄ đáy chiếu nghỉ    \  vế F4   /    đáy chiếu nghỉ ► C09    |
    |   LA4 ↕ 2495   [From EL +5250 To EL +7960 / 169.4mm x 16R]  2495 ↕ |  169.4mm x 16R = 2710
    |        LA3: 1150 | 280mm x 15T = 4200 (EQUAL TREADS) | 1150      |  (EQUAL RISERS)
    |   F13 ▼5250 ______________/ 33 35 37 …   ______________ ▼5250    |
    |                        …                                         |
  ▽ LEVEL 0 ______________/ 1 3 5 …                  [cửa 44]          |
            LA6: trục → tường … ; tổng (dưới mặt cắt)
```

- **Vế (flight)**: mỗi đoạn thang giữa hai sàn/chiếu nghỉ. Đặt tên theo chiều đi lên: **F1, F2, F3…** (`stair_section_info`).
- Vế **bị cắt** (vẽ biên dạng bậc, nét đậm) và vế **nhìn thấy phía sau** (nét mảnh). Cả hai đều có dim chiều cao (LA1); **chỉ vế bị cắt** có tag, số bậc và dim chiều dài (user, 2026-10-08).
- **Không đếm bậc trên hình.** Số cổ bậc (R), chiều cao cổ bậc, số bậc (T), độ sâu bậc lấy từ model.
- Mặt cắt **không có stair path** (stair path chỉ thể hiện trên mặt bằng).

## A. DIM

### Bố trí

| Phía | Từ trong ra ngoài |
|---|---|
| **Ngoài tường bên phải** (mẫu) | LA1: chuỗi chiều cao từng vế (công thức R) |
| **Ngoài tường bên trái** (mẫu) | LA2: chuỗi cao độ tầng (level → level) |
| **Trong lòng thang, sát tường hai bên** | LA4: chiều cao thông thuỷ dưới chiếu nghỉ / vế phía trên |
| **Trong lòng thang, ngang giữa từng vế** | LA3: chuỗi chiều dài vế (công thức T) |
| **Cuối vế, chỗ lan can** | LA5: chiều cao tay vịn (một chỗ đại diện) |
| **Dưới mặt cắt, ngoài sàn** | LA6: trục → mép tường → … → trục; tổng |

- LA1 và LA2 ở **hai phía khác nhau** (mẫu: LA1 phải, LA2 trái). Không dim cùng một chuỗi ở hai phía.
- Mỗi kích thước chỉ dim một lần. Dim không đè tag, số bậc, cao độ.

### LA1 — Chiều cao từng vế (đứng)

- **Chiều cao vế** = **số cổ bậc × chiều cao cổ bậc** (mẫu `169.4mm x 16R = 2710`, `169.4mm x 15R = 2540`).
  - Prefix `169.4mm x 16R = `, Below `(EQUAL RISERS)`. Giá trị là số đo sống, không Replace with text.
  - Chiều cao cổ bậc ghi **1 chữ số thập phân** như model; tích có thể lệch < 1 mm do làm tròn (169.375 × 16 = 2710) → bình thường.
  - Lệch lớn hơn → dim bám sai cao độ, hoặc model sai → báo user, không sửa model.
- **Chuỗi**: mặt hoàn thiện sàn/chiếu nghỉ → mặt hoàn thiện chiếu nghỉ/sàn kế tiếp, liên tục suốt chiều cao mặt cắt; mỗi đoạn là một vế.
- Tham chiếu: mặt trên chiếu nghỉ / sàn (hoặc level khi sàn hoàn thiện trùng level). Không tham chiếu detail line.

### LA2 — Cao độ tầng (đứng)

- Chuỗi level → level của mọi level nhìn thấy (mẫu `1500 | 5000 | 4000`), phía ngược với LA1.
- Dùng `level_dims_add` (chain + overall). View đã có chuỗi level → không thêm.

### LA3 — Chiều dài vế (ngang)

- **Chiều dài vế** = **số bậc × độ sâu mặt bậc**, prefix `280mm x 15T = `, Below `(EQUAL TREADS)` (cùng cách viết với SA2 của mặt bằng).
- **Số bậc = số cổ bậc − 1** khi bậc trên cùng ngang cao độ chiếu nghỉ / sàn (như mục C của mặt bằng, user 2026-10-08): 10R → `280mm x 9T = 2520`. Đoạn công thức đo từ **cổ bậc đầu** tới **cổ bậc cuối**; mặt bậc trên cùng (280) nằm trong đoạn chiếu nghỉ của chuỗi.
- **Chuỗi**: mép tường HT → [chiếu nghỉ] → mép bậc đầu → [vế: công thức] → mép bậc cuối → [chiếu nghỉ] → mép tường HT (mẫu `1150 | 280mm x 15T = 4200 | 1150`).
- Một chuỗi cho **mỗi vế bị cắt**, đặt **ngang khoảng giữa chiều cao vế đó**, trong khoảng trống giữa hai chiếu nghỉ. Không cắt qua tag vế, số bậc.
- Vế nhìn thấy phía sau có cùng chiều dài với vế cắt → không dim lại.

### LA4 — Chiều cao thông thuỷ dưới chiếu nghỉ (đứng)

- Từ **mặt hoàn thiện chiếu nghỉ / sàn** lên **đáy chiếu nghỉ (hoặc đáy vế) ngay phía trên** (mẫu `2495`, `2325`).
- Đặt **trong lòng thang, sát tường** hai bên (mẫu: trái và phải), mỗi chiếu nghỉ một dim.
- Mẫu **không** ghi `CLEAR`. Mặc định theo mẫu; nếu user muốn đồng bộ với mặt bằng (mọi kích thước thông thuỷ có `CLEAR`) → ghi vào profile.

### LA5 — Chiều cao tay vịn (đứng)
- **BẮT BUỘC** (user, 2026-10-08): **mỗi chiếu nghỉ bị cắt có một dim cao độ tay vịn**, từ mặt chiếu nghỉ lên đỉnh tay vịn đi dọc mép chiếu nghỉ (lan can P02 → 1200). Không được bỏ im lặng: tool không bắt được tham chiếu → ghi Việc tồn và xin user một dim tay làm mẫu (`la5FromDimId`).
- Làm: `stair_section_annotate {parts:["LA5"], la5Auto:true, la5RailingIds:[P02…], la5TargetMm:1200, la5LeftX, la5RightX}` (tự dò tham chiếu đỉnh tay vịn, chọn giá trị gần 1200 nhất và có vẽ). Giá trị lệch xa 1200 (vd. 1188, 1329) → kiểm bằng `rail_top_at`; không chắc thì không đặt, hỏi user.
- Kiểm: `stair_section_info` báo `LA5: landing … missing (mandatory)` cho từng chiếu nghỉ thiếu.
- **Tay vịn gắn tường P01 thấy rõ dọc chiếu nghỉ → thêm dim cao độ P01** (user, 2026-10-08): mỗi chiếu nghỉ bị cắt có đoạn P01 nằm ngang (gắn tường, nhìn thấy trên mặt cắt) có thêm một dim **mặt chiếu nghỉ → đỉnh tay vịn P01** (mẫu `900`), tham chiếu **Handrails** của P01 (không phải Top Rail), ngoài dim 1200 của P02. Đặt trên đoạn P01 thấy rõ, tách cột với dim 1200: chiếu nghỉ sát tường đối diện vế → cách dim 1200 khoảng 230–300 mm về phía tường; chiếu nghỉ phía kia → sát tường (cách mép tường HT ~500 mm). Có cả trên chiếu nghỉ đã có dim 1200.

- Từ **mũi bậc / mặt chiếu nghỉ** lên **đỉnh tay vịn** (mẫu `900`, ở cuối vế, sát chiếu nghỉ).
- Mẫu user 2026-10-08 (thang một nhánh, lan can trong 1200): **mỗi chiếu nghỉ một dim** từ mặt chiếu nghỉ lên đỉnh tay vịn trên cùng của lan can, đoạn chạy dọc mép chiếu nghỉ (vuông góc mặt cắt) → `1200`; dim đặt trên chiếu nghỉ, gần mép vế. Tay vịn ống nghiêng gắn tường không có đoạn ngang → không dim.
- ~~Tối thiểu một dim đại diện mỗi mặt cắt~~ (bỏ, 2026-10-08): thay bằng quy tắc bắt buộc mỗi chiếu nghỉ ở trên. Chiều cao lan can chiếu nghỉ khác tay vịn vế → thêm một dim cho lan can chiếu nghỉ.

### LA6 — Tường, cửa → trục (ngang, dưới mặt cắt)

- Như SA4 của mặt bằng: trục → hai mép tường hoàn thiện → mép cửa → … → trục; dòng ngoài là tổng.
- Chỉ trục host đang thấy. Không có trục → chuỗi dừng ở mép tường, báo user.

### Quy định chung cho dim

- Type: **type kiểm tra** của profile cho tới khi user duyệt.
- Cùng một cách viết công thức trong cả dự án: `169.4mm x 16R = `, `280mm x 15T = `.

## B. TAG

**Type tag lấy chung của dự án**: loại được dùng nhiều nhất **trong các mặt cắt** (`stair_section_info` → `RunTagTypes`). Không tạo type mới.

| # | Đối tượng | Số lượng | Vị trí (theo mẫu) |
|---|---|---|---|
| **LB1** | Vế thang — mẫu `From EL +2710 To EL +5250` / `169.4mm x 15R` | 1 tag / **vế bị cắt** (vế phía sau không tag, user 2026-10-08) | Khung chữ trong khoảng trống **dưới/cạnh vế**, **leader** ngắn vào vế. Mặt cắt ghi theo **cổ bậc (R)**; mặt bằng ghi theo bậc (T). |
| **LB2** | Tay vịn / lan can — mẫu `P01` | 1 tag / **vế bị cắt** cho lan can nhìn thấy rõ (user 2026-10-08: "bổ sung đầy đủ" — mỗi vế cắt một tag, ở đầu dưới vế, đầu tag ngay trên tay vịn, leader đứng). Tay vịn bị lan can lớn hơn che gần hết (vd. P01 gắn tường sau P02) → không tag (user, 2026-10-08). **P01 thấy rõ** (đoạn ngang gắn tường dọc chiếu nghỉ) → **tag P01** (user, 2026-10-08, mẫu: một tag trên mỗi chiếu nghỉ phía không có tag P02 gần đó) | Leader tới tay vịn, đầu tag trong khoảng trống phía trên vế, không đè nét bậc. P01: đầu tag cao hơn đỉnh tay vịn ~220–240 mm, **leader đứng** chạm đỉnh P01, x nằm giữa dim 900 và cột F../C.. |
| **LB3** | Cao độ (Spot Elevation, ký hiệu tam giác) — mẫu `2710`, `5250`, `7960`, `0` | Mỗi mặt chiếu nghỉ / sàn nhìn thấy, **ở mỗi đầu** có chiếu nghỉ (mẫu: cả trái và phải) | Trên mặt hoàn thiện, gần mép chiếu nghỉ, không đè tag F/C. |
| **LB4** | Hoàn thiện sàn — mẫu `F13` | 1 / mặt chiếu nghỉ hoặc sàn có LB3 | Phía trên mặt sàn, leader xuống mặt hoàn thiện, cạnh cao độ LB3. |
| **LB5** | Hoàn thiện trần / đáy chiếu nghỉ — mẫu `C09` | 1 / đáy chiếu nghỉ nhìn thấy | Phía dưới chiếu nghỉ, leader lên đáy. Thẳng hàng đứng với LB4 của cùng chiếu nghỉ. |
| **LB6** | Cửa đi, cửa sổ — mẫu `44`, `18` | đúng 1 / cửa nhìn thấy, **kể cả cửa nằm trong tường bị cắt** (user, 2026-10-08: tool chỉ thấy cửa nhìn chính diện, bỏ sót cửa trong tường cắt) | Theo `drafting-opening-tags.md` (T1–T8), **luôn có leader** trên mặt cắt thang (user, 2026-10-08): cửa nhìn chính diện → đầu tag trên đỉnh cửa ~190 mm, leader đứng vào trong cửa ~130 mm; cửa trong tường cắt → đầu tag **ngoài tường**, leader **ngang** vào cửa ở khoảng giữa chiều cao cửa. |

- Mỗi chủ thể **đúng một** tag; tag trùng → xoá bớt.
- Tag không đè dim, số bậc, tag khác, nét bậc.

## C. ĐẾM / ĐÁNH SỐ BẬC (LC)

- **Nguồn số liệu là model**: số cổ bậc (Actual Number of Risers), số bậc (Actual Number of Treads) của **từng vế**; chiều cao cổ bậc, độ sâu bậc của thang.
- **Kiểm tra chéo**: R × chiều cao cổ bậc = chiều cao vế (LA1); T × độ sâu = chiều dài vế (LA3).
- **Số bậc** trên vế bị cắt, theo mẫu:
  - **liên tục cả cầu thang** từ cổ bậc đầu tiên ở tầng thấp nhất (mẫu: F1 1…16, F2 17…31, F3 33…47, F4 49…);
  - hiện **cách một số** (số lẻ);
  - đặt sát mũi bậc, phía trên bậc;
  - **trùng với số trên mặt bằng** tại cùng một bậc (`drafting-stair-plan`, mục C).
- Vế nhìn thấy phía sau (nét mảnh): không đánh số.

## Mẫu tham chiếu (ảnh của user, 2026-10-07)

Mặt cắt dọc thang bộ, 1:50, bốn vế từ LEVEL 0 lên tới chiếu nghỉ +10500:

| Mục | Trên mẫu |
|---|---|
| LA1 (phải) | `169.4mm x 16R = 2710` · `169.4mm x 15R = 2540` · `169.4mm x 16R = 2710` (EQUAL RISERS) |
| LA2 (trái) | `1500 \| 5000 \| 4000` |
| LA3 | `… \| 280mm x 15T = 4200 (EQUAL TREADS) \| …`, một chuỗi mỗi vế |
| LA4 | `2495`, `2325`, `2495` hai bên |
| LA5 | `900` |
| LB1 | `From EL +0 To EL +2710 / 169.4mm x 16R` … `From EL +7960 To EL +10500 / 169.4mm x 15R` |
| LB2–LB6 | `P01` · `2710`, `5250`, `7960` · `F13` · `C09` · cửa `44`, `18` |
| LC | 1, 3, 5 … 51 |

Đã đối chiếu với view gốc của mẫu (2026-10-08, đọc bằng `view_dims_snapshot` + `stair_section_info`):

| Mục | Trên view gốc |
|---|---|
| LA3 | `1750 \| 280mm x 15T = 4200 \| 1750` (chiếu nghỉ \| vế \| chiếu nghỉ), mép tường HT → mép bậc đầu/cuối → mép tường HT; tổng tường–tường `7700` một lần ở vế dưới cùng. |
| LA6 | dưới: `160 \| 6930 \| 770 \| 160` (mép ngoài tường → mép HT → trục → mép HT → mép ngoài) + tổng `8020`; **trên đỉnh** mặt cắt thêm một chuỗi `1915 \| 5175 \| 930` + `8020` vì tường tầng trên khác tường tầng dưới. |
| LA2 | không chỉ level → level: chuỗi đi qua **ký hiệu cao độ cắt của các mặt bằng chi tiết thang** (Generic Annotation tam giác đánh số 1…7, nét đứt ngang lõi, đặt ở level + 1500 …) → `1500 \| 5000 \| 4000 \| 1500 \| 4000 \| 2800 …`; tổng toàn chiều cao ở cả hai phía. |
| LA4 | một cột đứng mỗi bên (cùng toạ độ ngang cho mọi tầng), mặt chiếu nghỉ → đáy chiếu nghỉ / đáy vế ngay trên. |
| LA5 | `900` ở đầu trên **mỗi vế bị cắt** (không chỉ một dim đại diện). |
| LB4 / LB5 | `F13` / `C09` là **Generic Annotation** (ô chữ, cùng family với tag hoàn thiện tường), không phải IndependentTag. |

## Kiểm tra sau khi làm

- `stair_section_info` lại: `Dims` đều `OK` (LA1, LA3, **LA5 từng chiếu nghỉ**), không còn `Issues`.
- `finish_marks_snap {mode:"survey"}`: mọi chấm F../C.. `OK`.
- `annotation_overlaps` cho view.
- **Danh mục ĐỦ — đánh dấu từng dòng trước khi báo xong** (thiếu dòng nào → làm, hoặc ghi Việc tồn kèm lý do; không bỏ im lặng):
  | Mục | Mỗi … |
  |---|---|
  | LA1 chiều cao vế (công thức R) | vế |
  | LA2 cao độ tầng + ký hiệu cao độ cắt | view |
  | LA3 chiều dài vế (công thức T = R − 1) | vế bị cắt |
  | LA4 thông thuỷ dưới chiếu nghỉ / sàn (kể cả chiếu nghỉ phía sau, sàn mái) | chiếu nghỉ + sàn, mỗi phía |
  | **LA5 cao độ tay vịn** (P02 1200 + P01 900 khi P01 thấy rõ) | **chiếu nghỉ bị cắt** |
  | LA6 tường / trục | view |
  | LB1 tag vế | vế bị cắt |
  | LB2 tag tay vịn P02 (đầu dưới vế, leader đứng) | vế bị cắt |
  | LB2 tag P01 thấy rõ (leader đứng) | chiếu nghỉ có P01 thấy rõ |
  | LB3 cao độ | chiếu nghỉ / sàn |
  | LB4 F.., LB5 C.. (chấm đúng mặt) | chiếu nghỉ / sàn |
  | LB6 cửa, có leader (cả cửa trong tường cắt) | cửa |
  | LC số bậc | vế bị cắt |
- Xuất ảnh sheet: mỗi vế một tag; công thức đúng R/T; số bậc liên tục và khớp mặt bằng; F/C/cao độ đủ ở mọi chiếu nghỉ.

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-07: chuẩn lập từ ảnh mẫu của user + chuẩn mặt bằng. `stair_section_info` đã biên dịch với RevitAPI 2023, **chưa chạy trên model thật**.
- 2026-10-08: chạy thật trên view gốc của mẫu. **Thang kéo (scissor)**: hai thang lồng nhau, vế bị cắt xen kẽ thuộc thang A rồi thang B; vế phía sau là vế "song sinh" cùng cao độ. Mẫu chỉ tag + đánh số **vế bị cắt**; vế phía sau không tag (LB1 đang ghi "cả vế phía sau" → chờ user chốt). Số bậc liên tục **theo cao độ** qua cả hai thang (F1 thang A 1–16, F4 thang B 17–31…).
- 2026-10-08: view mẫu dùng **Replace with text** cho công thức (`169.4mm x 16R = 2710`) → giá trị không sống. Không bắt chước: dùng prefix + below (`dims_text`). `stair_section_info` nay báo `Replace with text '…' (value not live)`; `view_dims_snapshot` nay trả cả `Above` và `Override`.
- 2026-10-08: lần chạy thật đầu tiên bằng `stair_section_annotate` (thang một nhánh, 6 vế, 3 vế cắt): dim/cao độ bám mặt 3D của **StairsRun / StairsLanding** được tạo nhưng **không vẽ** (Box null) → tham chiếu mặt của phần tử **Stairs**, như dim tay của dự án. Chữ công thức LA3 đặt giữa vế đè số bậc → hạ chuỗi LA3 xuống 30 % chiều cao vế, tag vế 8 %, dời chữ công thức sang vùng trống dưới vế (`dims_text_move`). Tay vịn gắn tường dạng ống nghiêng không có đoạn ngang → không có tham chiếu cho LA5 bằng API.
- 2026-10-08: lỗi hay gặp khi dim tay (thấy trên mẫu): một đoạn gộp hai vế (vế cắt + vế ngắn phía sau, vd. 15R + 6R = 3700) mà prefix ghi 15R; đoạn gộp vế + chiếu nghỉ (`5950` = 4200 + 1750) mang prefix `280mm x 15T =`; prefix thiếu dấu `=`; chuỗi có điểm gióng rác ngoài crop (đoạn `0`, đoạn hàng chục mét). Kiểm bằng `view_dims_snapshot` trước khi coi view mẫu là chuẩn.
- 2026-10-08: lõi nhiều thang chồng nhau (mỗi tầng một thang): **tag vế phải dùng type đọc cao độ theo tầng chân thang** — type của tầng dưới gắn lên thang tầng trên in "From EL +0 To EL +2839" (cao độ tính từ chân thang). Kiểm chữ tag (`stair_section_info` RunTags) và đổi type bằng `tags_retype`, như bảng type đã chốt cho mặt bằng.
- 2026-10-08: LA4 phải dừng ở **đáy sàn tầng** nằm giữa hai chiếu nghỉ cùng phía (không đo xuyên sàn). Cột LA4 đặt trong phạm vi chiếu nghỉ (không lấy theo tường xa khi chiếu nghỉ không chạm tường đó); khi mép chiếu nghỉ đã có cao độ, F/C, dim tay vịn → chọn x riêng từng dim theo chỗ trống, dời cao độ / F.. C.. vài trăm mm nếu cần (giữ trên mặt sàn, F và C cùng cột).
- 2026-10-08: lỗi chữ hay gặp khi sửa tay: `T` thay `R` ở LA1, chiều cao cổ bậc chép nhầm từ thang khác, `==`, Below `(EQUAL TREADS)` trên LA1, `(EQUAL TREATS)`, số bậc LA3 trừ 2 thay vì 1. Luôn so với `RiseText` / `GoingText` của `stair_section_info`.
- 2026-10-08 (user bổ sung tay trên mặt cắt lõi nhiều thang): LA4 tính cả **chiếu nghỉ nhìn thấy phía sau** — làm điểm chặn (chiếu nghỉ cắt → đáy chiếu nghỉ phía sau ngay trên, vd. `5521`) và làm điểm bắt đầu (chiếu nghỉ trên cùng, dù ở phía sau → đáy sàn mái, vd. `2400`). Không đo từ chiếu nghỉ này lên đáy chiếu nghỉ kia khi hai cái không chồng nhau theo phương ngang. Không đo xuyên qua vế phía sau.
- 2026-10-08 (ST lõi có thang khác nhìn xuyên qua): thang nằm ngoài lõi nhưng được view thu thập (chiếu nghỉ ở ngoài tường) → loại bằng `excludeStairIds` ở cả `stair_section_info` và `stair_section_annotate`, nếu không LA1 sẽ có mốc lạ (vd. 1412, 3000).
- 2026-10-08: dim cũ dùng Replace with text, đoạn 0 mm hoặc bám sai mặt (vế đo cả bậc trên cùng) → xoá bằng `dims_edit` (có log) và dựng lại bằng tool, không sửa chắp vá. Ô F../C.. thiếu ở chiếu nghỉ trên cùng: chép từ chiếu nghỉ cùng phía (`annot_copy_in_view`) hoặc `finish_mark_place` khi cần đổi chiều dài leader. Room tag đè tag vế → dời room tag (chỉ vị trí).
- 2026-10-08 (user: "point của tag sàn, trần chỉ sai vị trí"): **chấm đầu leader** của F.. phải nằm đúng **mặt trên** chiếu nghỉ / sàn, của C.. đúng **đáy** chiếu nghỉ / sàn; leader thứ hai (ngang) của C.. chạm **đáy nghiêng của vế** ở đúng độ cao đầu ô. Ô chép từ tầng khác thường lệch 10–115 mm → luôn chạy `finish_marks_snap` (survey) sau khi đặt/chép.
- 2026-10-08 (user: "điều chỉnh lại tag tay vịn cho đúng"): mỗi **vế bị cắt** một tag P02 ở **đầu dưới vế** (x = mũi bậc đầu − 30), leader **thẳng đứng** chạm **đỉnh tay vịn** (`rail_top_at`), đầu tag cao hơn 333–450 mm (tránh đường dim); bỏ tag P01 khi P01 bị P02 che; không tag giữa vế với leader gãy.
- 2026-10-08 (user bổ sung tay trên mặt cắt dọc thang một nhánh, chiếu nghỉ có tay vịn gắn tường P01 thấy rõ): (1) thêm **tag P01** (đầu tag trên đỉnh P01 ~230 mm, leader đứng) ở chiếu nghỉ không có tag P02 gần đó, và **dim 900** mặt chiếu nghỉ → đỉnh tay vịn P01 (tham chiếu Handrails) ở **mọi** chiếu nghỉ bị cắt, cạnh dim 1200 của P02; (2) **tag cửa trên mặt cắt luôn có leader**: cửa nhìn chính diện → leader đứng vào cửa; cửa nằm trong tường bị cắt (tool `elevation_opening_tags` không thấy) → tag ngoài tường, leader ngang vào cửa ở giữa chiều cao. Trước khi báo xong: liệt kê cửa của các tường bị cắt (room/level của lõi) và kiểm từng cửa có tag.
