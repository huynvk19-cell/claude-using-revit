---
name: drafting-stair-section-parallel
description: "Chuẩn triển khai MẶT CẮT thang bộ cắt SONG SONG với hướng đi (mặt cắt dọc vế thang): dim chiều cao vế có công thức (169.4mm x 16R = 2710 / EQUAL RISERS), dim cao độ tầng, dim chiều dài vế có công thức (280mm x 15T = 4200 / EQUAL TREADS), chiều cao thông thuỷ dưới chiếu nghỉ, chiều cao tay vịn, tường/cửa tới trục; tag vế, tay vịn, cao độ chiếu nghỉ, hoàn thiện sàn (F..) và trần/đáy chiếu nghỉ (C..), cửa; đánh số bậc liên tục. Stair section parallel to the stair path standard."
metadata:
  updated: "2026-10-07"
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
- Vế **bị cắt** (vẽ biên dạng bậc, nét đậm) và vế **nhìn thấy phía sau** (nét mảnh). Cả hai đều có tag và dim chiều cao.
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
- **Chuỗi**: mép tường HT → [chiếu nghỉ] → mép bậc đầu → [vế: công thức] → mép bậc cuối → [chiếu nghỉ] → mép tường HT (mẫu `1150 | 280mm x 15T = 4200 | 1150`).
- Một chuỗi cho **mỗi vế bị cắt**, đặt **ngang khoảng giữa chiều cao vế đó**, trong khoảng trống giữa hai chiếu nghỉ. Không cắt qua tag vế, số bậc.
- Vế nhìn thấy phía sau có cùng chiều dài với vế cắt → không dim lại.

### LA4 — Chiều cao thông thuỷ dưới chiếu nghỉ (đứng)

- Từ **mặt hoàn thiện chiếu nghỉ / sàn** lên **đáy chiếu nghỉ (hoặc đáy vế) ngay phía trên** (mẫu `2495`, `2325`).
- Đặt **trong lòng thang, sát tường** hai bên (mẫu: trái và phải), mỗi chiếu nghỉ một dim.
- Mẫu **không** ghi `CLEAR`. Mặc định theo mẫu; nếu user muốn đồng bộ với mặt bằng (mọi kích thước thông thuỷ có `CLEAR`) → ghi vào profile.

### LA5 — Chiều cao tay vịn (đứng)

- Từ **mũi bậc / mặt chiếu nghỉ** lên **đỉnh tay vịn** (mẫu `900`, ở cuối vế, sát chiếu nghỉ).
- Tối thiểu **một** dim đại diện mỗi mặt cắt. Chiều cao lan can chiếu nghỉ khác tay vịn vế → thêm một dim cho lan can chiếu nghỉ.

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
| **LB1** | Vế thang — mẫu `From EL +2710 To EL +5250` / `169.4mm x 15R` | 1 tag / vế nhìn thấy (cắt và phía sau) | Khung chữ trong khoảng trống **dưới/cạnh vế**, **leader** ngắn vào vế. Mặt cắt ghi theo **cổ bậc (R)**; mặt bằng ghi theo bậc (T). |
| **LB2** | Tay vịn / lan can — mẫu `P01` | 1 tag / lan can nhìn thấy (mỗi tầng một tag là đủ khi cùng loại) | Leader tới tay vịn, đầu tag trong khoảng trống phía trên vế, không đè nét bậc. |
| **LB3** | Cao độ (Spot Elevation, ký hiệu tam giác) — mẫu `2710`, `5250`, `7960`, `0` | Mỗi mặt chiếu nghỉ / sàn nhìn thấy, **ở mỗi đầu** có chiếu nghỉ (mẫu: cả trái và phải) | Trên mặt hoàn thiện, gần mép chiếu nghỉ, không đè tag F/C. |
| **LB4** | Hoàn thiện sàn — mẫu `F13` | 1 / mặt chiếu nghỉ hoặc sàn có LB3 | Phía trên mặt sàn, leader xuống mặt hoàn thiện, cạnh cao độ LB3. |
| **LB5** | Hoàn thiện trần / đáy chiếu nghỉ — mẫu `C09` | 1 / đáy chiếu nghỉ nhìn thấy | Phía dưới chiếu nghỉ, leader lên đáy. Thẳng hàng đứng với LB4 của cùng chiếu nghỉ. |
| **LB6** | Cửa đi, cửa sổ — mẫu `44`, `18` | đúng 1 / cửa nhìn thấy | Theo `drafting-opening-tags.md` (T1–T8). |

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

Chữ trên ảnh nhỏ: các đoạn chiếu nghỉ của LA3 và chuỗi LA6 đọc chưa rõ → đối chiếu khi chạy lần đầu, ghi bài học.

## Kiểm tra sau khi làm

- `stair_section_info` lại: `Dims` đều `OK`, không còn `Issues`.
- `annotation_overlaps` cho view.
- Xuất ảnh sheet: mỗi vế một tag; công thức đúng R/T; số bậc liên tục và khớp mặt bằng; F/C/cao độ đủ ở mọi chiếu nghỉ.

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-07: chuẩn lập từ ảnh mẫu của user + chuẩn mặt bằng. `stair_section_info` đã biên dịch với RevitAPI 2023, **chưa chạy trên model thật**.
