---
name: drafting-stair-section-cross
description: "Chuẩn triển khai MẶT CẮT thang bộ cắt NGANG vế (không song song stair path, vế thang nhìn thẳng đầu): chuỗi chiều cao từng dải vế có công thức (169.4mm x 16R = 2710 / EQUAL RISERS), cao độ tầng + ký hiệu cao độ cắt mặt bằng, chuỗi thông thuỷ | dày chiếu nghỉ liên tục, bề rộng vế tới tay vịn, tường tổng; tag vế chữ đứng ngoài tường, tag tay vịn, cao độ, F../C..; số bậc Start and End trên vế thấy cổ bậc. Stair cross-section standard."
metadata:
  updated: "2026-10-08"
  related: ["drafting-stair-section-parallel", "drafting-stair-plan", "drafting-opening-tags", "drafting-dimensions", "drafting-tools"]
---

# Mặt cắt thang bộ cắt ngang vế (Stair section across the flights)

Áp dụng cho **mặt cắt chi tiết thang bộ có đường cắt KHÔNG song song stair path** (thường vuông góc: nhìn thẳng vào đầu các vế; view tên thường STAIRCASE … SECTION B-B, tỉ lệ 1:50).
Không áp dụng cho:
- **mặt cắt dọc** (đường cắt song song stair path, vế chạy ngang bản vẽ): `drafting-stair-section-parallel`;
- **mặt bằng** lõi thang: `drafting-stair-plan`.

Tool: `stair_xsection_info` (read-only, kiểm theo chuẩn này), `level_dims_add`, `dims_at_positions`, `dims_text`, `annot_place`, `finish_marks_snap`, `stair_numbers_match`, `tags_retype`, `annotation_overlaps`. Skill: `drafting-stair-section-cross`.

Mã quy tắc: **X** (cắt ngang) — **XA** dim, **XB** tag, **XC** số bậc. Không dùng lại LA–LC (mặt cắt dọc) hay SA–SD (mặt bằng).

Nguồn: view mẫu của user (2026-10-08, lõi thang kéo hai thang lồng nhau, 0 → mái, đọc bằng `view_dims_snapshot`, `view_elem_boxes`, `number_systems_info`, `tag_leaders_info`) + các quy tắc đã chốt ở mặt cắt dọc (số liệu từ model, công thức có giá trị sống, type lấy chung dự án, mỗi chủ thể một tag).

## 0. Đọc mặt cắt trước khi làm

```text
   XA2 (cao độ tầng                                          XA1 (chiều cao từng dải)
   + ký hiệu cắt mặt bằng)                                   + tổng
     |  [From EL…]→ |  F13 ●            ║            | ←[From EL…]  |  176.6mm x 16R = 2826
     |  (chữ đứng)  |___________________║____________|  (chữ đứng)  |  (EQUAL RISERS)
     |              |▓▓▓ chiếu nghỉ cắt ▓▓▓▓▓▓▓▓▓▓▓▓▓|  ← 215       |
     |              |  C09 ●            ║  ═══ 63    |              |
     |              |  (đáy vế: trống)  ║  ═══ cổ bậc|  ↕ XA4 2611  |
     |  P01 ●─      | ─● P01      P01 ●─║  ═══ 47    |              |
     |    70 | 1745 | 70 | 320 | 70 | 1745 | 70  (XA3)               |
   ▽ LEVEL 0  160 | 1885 | 320 | 1885 | 160 ; 4410 (XA5, dưới mặt cắt)
```

- Mặt cắt chia lõi thành **hai nửa** (trái / phải), mỗi nửa một vế ở mỗi dải cao độ (thang kéo: hai thang lồng nhau; thang hai vế: vế lên và vế xuống).
- **Hướng vế** (`stair_xsection_info` → `Facing`):
  - `away` — vế đi lên **ra xa** người nhìn: thấy cổ bậc và mũi bậc (nét ngang dày đặc) → **có số bậc**;
  - `toward` — vế đi lên **về phía** người nhìn: chỉ thấy mặt đáy vế (vùng trống) → **không số bậc**, nhưng vẫn có tag vế.
- **Dải (band)**: khoảng cao độ chân vế → đầu vế. Hai vế cùng khoảng cao độ (thang kéo) là **một dải**, dim một lần.
- **Chiếu nghỉ / sàn bị cắt**: sàn hatch nằm ngang lõi. Chiếu nghỉ có thể chỉ bị cắt ở một nửa (vd. chiếu nghỉ trên cùng). Chiếu nghỉ nhìn thấy phía sau không chặn chuỗi XA4.
- **Không đếm bậc trên hình**: R, chiều cao cổ bậc lấy từ model. Mặt cắt **không có stair path**.

## A. DIM

### Bố trí

| Phía | Từ trong ra ngoài |
|---|---|
| **Ngoài tường bên phải** (mẫu) | XA1: chuỗi chiều cao từng dải (công thức R); ngoài cùng: tổng chân → mái |
| **Ngoài tường bên trái** (mẫu) | XA2: chuỗi cao độ tầng qua ký hiệu cao độ cắt mặt bằng (1…7) |
| **Trong lòng thang, nửa có vế `away`** | XA4: chuỗi đứng liên tục thông thuỷ \| dày chiếu nghỉ, từ sàn tầng thấp nhất lên đáy sàn mái |
| **Ngang lõi, khoảng trống trên một chiếu nghỉ của mỗi khối thang** | XA3: bề rộng vế tới tay vịn |
| **Dưới mặt cắt** (và một dòng tổng trên đỉnh) | XA5: tường → tường; trục nếu có |

- XA1 và XA2 ở **hai phía khác nhau**. Không dim một chuỗi ở hai phía.
- Mỗi kích thước chỉ dim một lần (mẫu có hai dòng tổng trùng nhau ở đỉnh → lỗi).

### XA1 — Chiều cao từng dải (đứng)

- Như LA1 của mặt cắt dọc nhưng **theo dải**: mỗi đoạn = một dải = **R × chiều cao cổ bậc**: prefix `169.4mm x 16R = `, Below `(EQUAL RISERS)`, giá trị sống. Không Replace with text (R9).
- Chuỗi: mặt sàn tầng thấp nhất → mặt chiếu nghỉ / sàn ở mỗi đầu dải → … → mặt chiếu nghỉ / sàn trên cùng. Mỗi đầu dải phải có điểm gióng: **không gộp hai dải vào một đoạn** (mẫu: đoạn `3700` = dải 6R + dải 15R mang prefix 15R → sai).
- Đoạn từ chiếu nghỉ trên cùng lên mái (không phải vế) không mang công thức.
- Ngoài cùng: một dòng tổng chân → mái.
- Tham chiếu: mặt trên chiếu nghỉ / sàn trên phần tử **Stairs** / Floor (như dim tay của dự án), level khi sàn hoàn thiện trùng level.

### XA2 — Cao độ tầng (đứng)

- Như LA2: chuỗi qua level và **ký hiệu cao độ cắt của các mặt bằng chi tiết thang** (Generic Annotation tam giác đánh số, nét đứt ngang lõi) → `1500 | 5000 | 4000 | 1500 | …`; dòng tổng ngoài.
- `level_dims_add` khi chưa có. Chuỗi có đoạn 0 mm (điểm gióng rác) → xoá, dựng lại.

### XA3 — Bề rộng vế tới tay vịn (ngang)

- Chuỗi: mép tường HT → mép tay vịn → mép tay vịn → mép tường giữa → mép tường giữa → mép tay vịn → mép tay vịn → mép tường HT (mẫu `70 | 1745 | 70 | 320 | 70 | 1745 | 70`): thấy ngay **bề rộng thông thuỷ giữa hai tay vịn** và phần tay vịn nhô ra.
- **Một chuỗi cho mỗi khối thang** (mỗi bộ thang / mỗi đoạn tầng có bề rộng hoặc tay vịn khác: mẫu ba chuỗi cho ba khối thang). Nửa nào không có tay vịn ở khối đó → chuỗi tới mép tường (mẫu `1885 | 320 | 70 | 1745 | 70`).
- Đặt trong khoảng trống phía trên một chiếu nghỉ, không cắt qua tag, số bậc, ô F../C...
- Tham chiếu: mặt tường, **Handrails** của tay vịn, mép phần tử Stairs. Không bám link.

### XA4 — Thông thuỷ | dày chiếu nghỉ (đứng, một chuỗi liên tục)

- Khác mặt cắt dọc (LA4 là các dim rời mỗi chiếu nghỉ): ở mặt cắt ngang là **một chuỗi liên tục** trong lòng thang, xen kẽ:
  `thông thuỷ (mặt chiếu nghỉ / sàn → đáy chiếu nghỉ / sàn kế trên) | dày chiếu nghỉ (đáy → mặt)` → mẫu `2495 | 215 | 2325 | 215 | 2495 | 215 | … | 5270 | 150` (đoạn cuối: đáy sàn mái + dày sàn mái).
- Chỉ dừng ở **chiếu nghỉ / sàn bị cắt có đi qua cột dim**: chiếu nghỉ chỉ cắt ở nửa kia hoặc chiếu nghỉ nhìn thấy phía sau → chuỗi đi thẳng qua (mẫu: đoạn `3485`, `5270`).
- Đặt ở nửa có vế `away` (mẫu: khoảng 2/3 bề rộng nửa đó, giữa vùng số bậc và tường), thẳng một cột suốt chiều cao.
- Mẫu **không** ghi `CLEAR` (như LA4).
- Kiểm: `stair_xsection_info` → `ClearHeights` (giá trị mong đợi từng đoạn, từ model).

### XA5 — Tường → tường (ngang)

- Dưới mặt cắt: mép ngoài tường → mép HT → mép tường giữa → … → mép ngoài (mẫu `160 | 1885 | 320 | 1885 | 160`), dòng tổng `4410` bên ngoài. Có trục host đang thấy → chuỗi đi qua trục (như LA6 / SA4).
- Trên đỉnh: **một** dòng tổng (không hai dòng trùng). Không bám link.

### XA6 — Cao độ tay vịn

- Mẫu mặt cắt ngang **không có**; chiều cao tay vịn đã dim ở mặt cắt dọc (LA5 bắt buộc).
- **Chờ user chốt** có cần trên mặt cắt ngang không. Tới khi chốt: không làm, ghi Cần xem.

### Quy định chung cho dim

- Type: **type kiểm tra** của profile cho tới khi user duyệt.
- Một cách viết công thức cho cả dự án (như mặt cắt dọc). So chữ với `RiseText` của `stair_xsection_info`.

## B. TAG

**Type tag lấy chung của dự án**: loại tag vế dùng nhiều nhất **trong các mặt cắt** (đọc theo cổ bậc R; mỗi khối tầng một type, xem profile). Không tạo type mới.

| # | Đối tượng | Số lượng | Vị trí (theo mẫu) |
|---|---|---|---|
| **XB1** | Vế thang — `169.4mm x 16R` / `From EL +0 To EL +2710` | **1 tag / vế nhìn thấy, cả hai nửa** (`away` và `toward`) | Khung chữ **xoay đứng**, đặt **ngoài tường của nửa chứa vế** (vế nửa trái → ngoài tường trái, nửa phải → ngoài tường phải), ngang giữa dải; **leader ngang** từ khung vào giữa vế. Chữ cao độ phải đúng From/To EL của vế (sai → type sai khối tầng, `tags_retype`). |
| **XB2** | Tay vịn / lan can — `P01`, `P02` | Mỗi tay vịn **thấy rõ** ở mỗi dải (tay vịn gắn tường biên, tay vịn hai mặt tường giữa); lan can `P02` ở tầng trên cùng | Đầu tag trong lòng thang cạnh tay vịn, **leader ngang** ngắn, chấm chạm tay vịn. Tay vịn bị lan can lớn hơn che → không tag (như LB2). |
| **XB3** | Cao độ (Spot Elevation) | Mỗi mặt chiếu nghỉ / sàn **bị cắt** (mặc định theo mặt cắt dọc LB3); **chờ user chốt** — mẫu chỉ có ở sàn tầng, chiếu nghỉ trên cùng và mái | Trên mặt hoàn thiện, ở phần chiếu nghỉ bị cắt, không đè ô F../C... |
| **XB4** | Hoàn thiện sàn `F13` | 1 / mặt chiếu nghỉ hoặc sàn bị cắt; thêm ở mặt chiếu nghỉ thấy được của nửa kia khi mặt đó lộ ra (mẫu) | Phía trên mặt sàn, leader đứng xuống, **chấm đúng mặt trên**; cột giữa nửa `toward` (mẫu). |
| **XB5** | Hoàn thiện trần / đáy chiếu nghỉ `C09` | 1 / đáy chiếu nghỉ bị cắt; đáy sàn mái (mẫu: hai ô) | Dưới đáy, leader đứng lên, **chấm đúng đáy**, **cùng cột** với XB4 của chiếu nghỉ đó. |
| **XB6** | Cửa | như LB6 (`drafting-opening-tags.md`, luôn có leader) | — |

- F../C.. là Generic Annotation (ô chữ), không phải IndependentTag: kiểm chấm bằng `finish_marks_snap` (survey).
- Room tag (nếu có ở tầng mái) không đè tag, dim.
- Mỗi chủ thể **đúng một** tag; tag không đè dim, số bậc, tag khác.

## C. SỐ BẬC (XC)

- **Chỉ trên vế `away`** (thấy cổ bậc). Vế `toward` không đánh số.
- Thiết lập (mẫu): **Display Rule = Start and End** (chỉ số đầu và số cuối của vế — các số ở giữa chồng lên nhau khi nhìn thẳng đầu vế), Tag Type Riser, Reference Right Quarter, Justify Back, Number Size 3.0 mm. Khác mặt bằng / mặt cắt dọc (Odd).
- Số **liên tục theo cao độ qua cả lõi** (thang kéo: dải hai vế tính một lần) và **trùng số trên mặt bằng / mặt cắt dọc** tại cùng bậc.
- `stair_xsection_info` → `FirstNumber` (từ Tread/Riser Start Number của thang) so với `FirstByElevation`. Lệch → so với mặt bằng trước, rồi mới sửa (đổi start number là sửa thuộc tính thang: hỏi user, R1).

## Mẫu tham chiếu (view mẫu của user, 2026-10-08)

Lõi thang kéo, 1:50, 18 vế, 0 → mái +30563:

| Mục | Trên mẫu |
|---|---|
| XA1 (phải) | `169.4mm x 16R = 2710` · `169.4mm x 15R = 2540` … `176.6mm x 16R = 2826` · `176.2mm x 15R = 2643` (EQUAL RISERS); tổng `30413` ngoài cùng |
| XA2 (trái) | `1500 \| 5000 \| 4000 \| 1500 \| 4032 \| 2768 \| 1500 \| 5781 \| 1119 \| 1500 \| 1713` |
| XA3 | `70 \| 1745 \| 70 \| 320 \| 70 \| 1745 \| 70` ×2 khối dưới, `1885 \| 320 \| 70 \| 1745 \| 70` khối trên |
| XA4 | `2495 \| 215 \| 2325 \| 215 \| … \| 2428 \| 215 \| 3485 \| 215 \| 5270 \| 150`, một cột ở nửa phải |
| XA5 | dưới `160 \| 1885 \| 320 \| 1885 \| 160` + `4410`; trên `4410` |
| XB1 | 18 tag (mọi vế), chữ đứng, ngoài tường hai bên, leader ngang |
| XB2 | `P01` mỗi tay vịn thấy ở mỗi dải, `P02` tầng mái, leader ngang |
| XB3 | chỉ `0`, `18800`, `27962`, `30563` |
| XB4 / XB5 | `F13` trên, `C09` dưới mỗi chiếu nghỉ bị cắt, cùng cột |
| XC | vế nửa phải: `1 16`, `17 31`, `32 47`, … (Start and End) |

Lỗi thấy trên mẫu (không bắt chước): công thức XA1 dùng Replace with text; một đoạn gộp hai dải (6R + 15R) mang prefix 15R, thiếu điểm gióng ở hai đầu dải trên cùng; chuỗi cao độ phụ có ba đoạn 0 mm; hai dòng tổng trùng nhau ở đỉnh; một dim ngang bám vào link; start number của thang tầng trên không nối tiếp thang dưới.

## Kiểm tra sau khi làm

- `stair_xsection_info` lại: `Dims` XA1 đều `OK`, XA2/XA3 `OK`, `ClearHeights` một nửa đủ, không còn `Issues` trừ mục user đã duyệt.
- `finish_marks_snap {mode:"survey"}`: mọi chấm F../C.. `OK`.
- `annotation_overlaps` cho view; xuất ảnh **view**.
- **Danh mục ĐỦ** (thiếu dòng nào → làm, hoặc ghi Việc tồn kèm lý do):
  | Mục | Mỗi … |
  |---|---|
  | XA1 chiều cao dải (công thức R) + tổng | dải |
  | XA2 cao độ tầng + ký hiệu cao độ cắt | view |
  | XA3 bề rộng tới tay vịn | khối thang |
  | XA4 thông thuỷ \| dày chiếu nghỉ, liên tục | view (một nửa) |
  | XA5 tường → tường (+ trục) | view |
  | XB1 tag vế | vế nhìn thấy (cả hai nửa) |
  | XB2 tag tay vịn / lan can | tay vịn thấy rõ mỗi dải |
  | XB3 cao độ | chiếu nghỉ / sàn bị cắt (chờ chốt) |
  | XB4 F.., XB5 C.. (chấm đúng mặt) | chiếu nghỉ / sàn bị cắt |
  | XB6 cửa có leader | cửa |
  | XC số bậc Start and End | vế `away` |

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-08: chuẩn lập từ view mẫu mặt cắt ngang của user + chuẩn mặt cắt dọc. `stair_xsection_info` chạy thật trên view mẫu: XA4 mong đợi khớp 20/20 đoạn của chuỗi tay; nhận đúng mặt cắt dọc (trả lỗi, chuyển sang `stair_section_info`). Chưa có lệnh annotate riêng: dựng dim bằng `dims_at_positions` / dim tay mẫu, tag bằng `annot_place`.
- 2026-10-08 (API): `Dimension.Origin` ném lỗi với dim nhiều đoạn → lấy vị trí từ `(Line)d.Curve`. Bounding box của StairsLanding trùm cả thang → độ dày chiếu nghỉ lấy từ hình học (mặt trên → mặt dưới thấp nhất; ngay dưới mặt trên có mặt 15 mm của lớp hoàn thiện). `SpotDimension.ValueString` rỗng → so cao độ bằng `Origin.Z`.
