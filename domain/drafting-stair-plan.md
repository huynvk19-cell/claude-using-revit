---
name: drafting-stair-plan
description: "Chuẩn triển khai chi tiết MẶT BẰNG lõi thang bộ (không dùng cho mặt cắt thang): dim thông thuỷ vế thang (CLEAR), dim chiều dài vế thang kèm công thức (280mm x 14T = 3920), dim chiếu nghỉ, dim tường/cửa tới trục; tag vế thang, tay vịn, cao độ, cửa, hoàn thiện sàn và tường; đếm/đánh số bậc từng vế; stair path chỉ có mũi tên. Stair core plan detailing standard."
metadata:
  updated: "2026-10-06"
  related: ["drafting-grid-dims", "drafting-opening-tags", "drafting-annotation", "drafting-tools", "drafting-api-pitfalls", "drafting-work-rules"]
---

# Mặt bằng lõi thang bộ (Stair core plan)

Áp dụng cho **mặt bằng chi tiết lõi thang bộ** (view tên thường có STAIRCASE / STAIR CORE, tỉ lệ 1:50 hoặc 1:25).
Không áp dụng cho:
- key plan thang thoát hiểm (sơ đồ chỉ dẫn);
- **mặt cắt thang**: chuẩn riêng. Cắt dọc vế (song song stair path) → `drafting-stair-section-parallel` (mã LA–LC); cắt ngang vế → chưa viết. Không dùng lại SA–SD.

Tool: `stair_plan_audit` (read-only), `stair_plan_annotate` (path, số bậc, tag vế), `dims_text` (CLEAR, công thức). Skill: `drafting-stair-plan`.

Quy tắc từ user (2026-10-06): **A** dim, **B** tag, **C** đếm bậc, **D** stair path. Mã quy tắc ghi kèm chữ **S** (SA1, SB3…) để không trùng với Q, T, G của các chuẩn khác.

## 0. Đọc mặt bằng lõi thang trước khi làm

### Ba vế thang trên một mặt bằng

Mặt bằng tầng điển hình thường thấy **3 vế thang**. Đi theo chiều **đi lên**:

| Thứ tự | Vế | Nhìn thấy | Thuộc thang |
|---|---|---|---|
| **V1** | vế đầu của thang tầng dưới | **một nửa**, phần nằm **sau nét cắt** | thang tầng dưới |
| **V2** | vế sau của thang tầng dưới (chiếu nghỉ → sàn tầng này) | **đầy đủ** | thang tầng dưới |
| **V3** | vế đầu của thang tầng này (sàn tầng này → chiếu nghỉ trên) | **một nửa**, phần **trước nét cắt** | thang tầng này |

```text
            tường đầu (vuông góc hướng đi)
  +-------------------------------------------+
  |        chiếu nghỉ (của thang tầng dưới)    |
  |  +---------------+   +---------------+     |
  |  | V1 ↑          |   |               |     |
  |  | (sau nét cắt) |   | V2 ↓ (đầy đủ) |     |   tường bên (song song hướng đi)
  |  |~~~ nét cắt ~~~| L |               |     |   L = lan can giữa hai vế
  |  | V3 ↑          |   |               |     |
  |  | (trước nét cắt)   |               |     |
  |  +---------------+   +---------------+     |
  |        sàn tầng này / cửa buồng thang      |
  +-------------------------------------------+
  Đi lên: V1 → chiếu nghỉ → V2 → sàn tầng này → V3 (mũi tên trên bản vẽ chỉ chiều đi lên)
```

- **V1 và V3 nằm chung một dải (lane)**, ngăn bởi nét cắt. Hai vế này thuộc **hai thang khác nhau** và có thể khác số bậc.
- Tầng thấp nhất thường chỉ có V3. Tầng trên cùng thường chỉ có V1 + V2 (V1 thấy đầy đủ vì không có vế nào phía trên).
- **Không đếm vế bằng mắt.** Xác định V1/V2/V3 bằng `stair_plan_audit`: so cao độ vế với mặt cắt của view và độ chồng lên nhau của footprint.

### Hai phương

- **Hướng đi** (along): dọc theo vế thang.
- **Phương ngang** (across): vuông góc hướng đi, là phương đo bề rộng vế.
- **Tường song song hướng đi** = tường hai bên. **Tường vuông góc hướng đi** = tường hai đầu (phía chiếu nghỉ và phía sàn tầng / cửa).

### Mép hoàn thiện

- **Mép tường hoàn thiện** = mặt tường **gần lòng thang nhất**, đã gồm lớp hoàn thiện (lớp trát, ốp). Nếu hoàn thiện vẽ bằng tường riêng thì lấy mặt của tường hoàn thiện đó.
- **Mép trong tay vịn** = mép tay vịn quay về **phía lòng vế thang**.

## A. DIM

**Mọi kích thước thông thuỷ đều có suffix `CLEAR`** (Suffix của từng đoạn dim, không Replace with text): SA1 bề rộng vế, SA1 tường–tường, SA2 tường–tường, SA3 chiếu nghỉ / chiếu tới.

### Bố trí dim (theo mẫu của user, 2026-10-06)

```text
  SA2  SB1        tường đầu (phía chiếu nghỉ)        SB1  SA2
   |    |   +-----------------------------------+    |    |
   |    |   |     SA3: 1965 CLEAR (dọc giếng)   |    |    |
   |    |   |  SA1: 70|1550 CLEAR|80|300|80|... |    |    |
   |    +-->|  [ V1 / V3 ]   lan can   [ V2 ]   |<---+    |
   |        |  SA1: 70|1550 CLEAR|80|300|80|... |         |
   |        |     SA3: 1965 CLEAR (chiếu tới)   |         |
   |        +-----------------------------------+         |
                 tường đầu (phía sàn tầng / cửa)
                 SA4: trục → mép tường … ; tổng
```

- **SA2**: chuỗi `2205 | 280mm x 14T = 3920 (EQUAL TREADS) | 1805 | 400`, dòng ngoài là tổng `8330`.
- **SB1**: tag vế nằm **giữa tường và SA2**, leader đi vào vế.

| Phía | Từ trong ra ngoài |
|---|---|
| **Trong lòng thang** | SA1 (chuỗi phương ngang, sát hai đầu vế) · SA3 (dọc trục giếng thang, trên chiếu nghỉ và chiếu tới) |
| **Ngoài tường bên** (song song hướng đi) | tag vế SB1 → chuỗi SA2 → tổng SA2 |
| **Ngoài tường đầu** (vuông góc hướng đi) | SA4: trục → mép tường → … ; tổng |

- Mỗi kích thước chỉ dim **một lần**. Không dim cùng một chuỗi ở hai phía (trừ SA1 lặp ở hai đầu vế như mẫu).
- Khoảng cách giữa các dòng dim đều nhau (mặc định 7 mm giấy). Chuỗi SA2 phải nằm **ngoài** tag vế SB1, không đè tag.

### SA1 — Bề rộng thông thuỷ vế thang (phương ngang)

- **Bề rộng thông thuỷ** một vế = khoảng cách giữa **mép trong hai tay vịn** của vế đó.
  - Phía không có tay vịn → tính tới **mép tường hoàn thiện**.
- **Chuỗi**, ví dụ lõi chữ U có lan can giữa (mẫu: `70 | 1550 CLEAR | 80 | 300 | 80 | 1550 CLEAR | 70`):

  `tường HT → [tay vịn tường nhô ra] → mép trong tay vịn → [vế 1 CLEAR] → mép trong tay vịn giữa → [tay vịn] → [giếng thang] → [tay vịn] → mép trong tay vịn giữa → [vế 2 CLEAR] → mép trong tay vịn tường → [tay vịn tường] → tường HT`

  - Đoạn tường HT → mép trong tay vịn (70) = phần tay vịn tường nhô ra. Không có tay vịn tường thì không có đoạn này.
  - Lan can giữa tách thành **tay vịn | giếng | tay vịn** (80 | 300 | 80).
- **Tổng**: **tường HT → tường HT** giữa hai tường **song song hướng đi** + `CLEAR`.
- **Vị trí**: **trong lòng thang**, đường dim sát đầu vế: một chuỗi phía chiếu nghỉ, một chuỗi phía chiếu tới (mẫu có cả hai). Không đè số bậc, mũi tên, tag.

### SA2 — Chiều dài vế thang (hướng đi)

- **Chiều dài vế** = **số bậc × độ sâu mặt bậc**.
- Đoạn dim chiều dài vế **có công thức**:
  - Prefix `280mm x 14T = ` → hiện `280mm x 14T = 3920`.
  - Below `(EQUAL TREADS)`.
  - 280 = độ sâu mặt bậc (mm), 14T = số bậc (T = tread), 3920 = giá trị dim đo được.
  - **Một cách viết cho cả dự án**: `280mm x 14T = `. Không dùng `280x13T= ` (mẫu có lẫn hai kiểu → sửa về một kiểu).
  - Số bậc lấy từ **model** (mục C), không đếm nét.
  - Giá trị dim phải **bằng** số bậc × độ sâu (lệch ≤ 1 mm). Lệch → dim đang bám sai mép → sửa tham chiếu, không sửa chữ.
- **Chuỗi** (đo theo hướng đi), mẫu `2205 | 280mm x 14T = 3920 | 1805 | 400`:

  `tường HT đầu → [chiếu nghỉ tới mép tường] → mép bậc cuối → [vế: công thức] → mép bậc đầu → [chiếu tới] → (trục đi qua lòng thang, nếu có) → tường HT đầu kia`

  - Phải có đoạn **bề rộng chiếu nghỉ tới mép tường**.
  - **Trục đi qua lòng thang** (vd. trục F) được chèn vào chuỗi (mẫu: 1805 | 400).
- **Tổng** (dòng ngoài, mẫu 8330): **tường HT → tường HT** giữa hai tường **vuông góc hướng đi** + `CLEAR`.
- **Vị trí**: ngoài **tường bên**. Mỗi dải vế dim ở **phía tường gần nó**:
  - lõi chữ U: dải V1/V3 dim ở tường bên phía nó, dải V2 dim ở tường bên phía còn lại.
- **Dải V1/V3** chỉ có **một** dim chiều dài:
  - V1 và V3 cùng số bậc, cùng độ sâu, cùng mép đầu/cuối (± 5 mm) → một dim, một công thức.
  - Khác nhau → dim theo **V3** (vế của tầng này), báo V1 trong "Cần xem".

### SA3 — Chiếu nghỉ, chiếu tới

- Mỗi **chiếu nghỉ nhìn thấy** và **chiếu tới** (phần sàn tầng trong lõi thang) cần **bề rộng thông thuỷ** + `CLEAR` (mẫu: `1965 CLEAR`, hai chỗ).
- Đo theo hướng đi, từ **mép trong tay vịn** (đầu lan can giữa) tới **mép tường hoàn thiện** (hoặc mép tay vịn tường).
- Đặt **trong lòng thang**, đường dim **dọc trục giếng thang** (giữa hai vế), không cắt qua chữ cao độ, tag, mũi tên.

### SA4 — Tường bao, cửa đi, cửa sổ → trục

- Chuỗi từ **trục** → **hai mép tường hoàn thiện** của từng tường bao (thể hiện bề dày tường) → **hai mép** cửa đi / cửa sổ → … → **trục** (mẫu: `300 | 3720`, tổng `4020`).
- Chỉ trục **đang thấy** trên view. Trục host, không dùng trục link.
- Cửa: tham chiếu vào chính cửa (như Q5 của `drafting-opening-dims.md`).
- Nằm **ngoài** các dim vế thang.
- Phía có tường mà không có trục nào → chuỗi dừng ở mép tường cuối, báo user.

### Quy định chung cho dim

- Type: **type kiểm tra** của profile cho tới khi user duyệt.
- Đoạn ngắn (70, 80): để Revit đẩy chữ ra ngoài, hoặc dời chữ; chữ không đè chữ.
- Không đè lên tag, chữ cao độ, mũi tên stair path, số bậc.

## B. TAG

**Type tag lấy chung của dự án**: loại tag của category đó được dùng **nhiều nhất trong dự án** (`stair_plan_audit` liệt kê). Không tạo type mới. Category không có tag nào trong dự án → hỏi user.

| # | Đối tượng | Số lượng | Vị trí (theo mẫu) |
|---|---|---|---|
| **SB1** | Vế thang (Stair Run Tag) — mẫu: `From EL +5250 To EL +7875` / `280mm x 14T` | 1 tag / vế nhìn thấy (thường 3: V1, V2, V3) | **Ngoài tường bên** của dải vế, **giữa tường và chuỗi SA2**, chữ chạy dọc vế, **leader có mũi tên** đi qua tường vào **phần nhìn thấy** của vế. V1 và V3 cùng dải → hai tag cùng phía, mỗi tag ngang phần nhìn thấy của vế mình. Không bị dim đè. |
| **SB2** | Tay vịn / lan can (Railing Tag) — mẫu: `P01` tay vịn tường, `P02` lan can giữa | 1 tag / lan can (mỗi tay vịn tường một `P01`) | **Leader** ngắn tới lan can. Đầu tag **không nằm trên nét vế thang**: giếng thang, chiếu nghỉ hoặc khoảng trống cạnh lan can. *(Mẫu có tag đặt đè nét bậc → không làm theo.)* |
| **SB3** | Cao độ (Spot Elevation) | 1 / chiếu nghỉ nhìn thấy (mẫu `2625`); 1 / chiếu tới trong lõi thang (mẫu `5250`, `0.000`); 1 sàn **ngoài cửa buồng thang** | Trên mặt chiếu nghỉ / mặt sàn. Không trên cung mở cửa, không trên mũi tên path. |
| **SB4** | Cửa đi, cửa sổ (Door/Window Tag) | đúng 1 / cửa nhìn thấy | Sát cửa, không đè dim (SA4), không nằm trên cung mở cửa. |
| **SB5** | Hoàn thiện sàn — mẫu: `F13` | 1 / chiếu nghỉ và 1 / chiếu tới | Cùng cụm với cao độ SB3 của sàn đó, không đè path, không đè dim SA3. |
| **SB6** | Hoàn thiện tường bao lõi thang | ≥ 1 / loại hoàn thiện tường | **Leader gần chủ thể nhất có thể.** Đầu tag có thể **dồn vào góc tường** (góc trong lõi thang còn trống). |

- Tag nào cũng phải: gần chủ thể, không đè dim, không đè tag khác, không nằm trên nét vế thang.
- Mỗi chủ thể **đúng một** tag. Tag trùng → xoá bớt (giữ tag đặt tốt hơn).
- SB5/SB6 dùng loại tag mà dự án đang dùng cho hoàn thiện (mẫu: tag ô vuông mã `F..`). Dự án chưa thống nhất → hỏi, ghi vào profile.

## C. ĐẾM SỐ BẬC

- **Nguồn số liệu duy nhất là model**: số bậc (Actual Number of Treads), số cổ bậc (Actual Number of Risers) của **từng vế**, độ sâu mặt bậc (Actual Tread Depth) của thang chứa vế đó.
- **Không** đếm nét trên mặt bằng: V1 và V3 chỉ hiện một nửa, nét cắt che bậc.
- Đếm **riêng từng vế** V1, V2, V3. V1/V3 thuộc hai thang khác nhau → có thể khác số bậc (mẫu: tầng trệt `175.0mm x 15R`, tầng điển hình `280mm x 14T`, tầng trên `280mm x 13T`).
- **Kiểm tra chéo**: số bậc × độ sâu = chiều dài vế đo trên footprint (± 1 mm). Lệch → báo user, không tự sửa model.
- **Đánh số bậc** (Stair Tread/Riser Number), theo mẫu:
  - số **liên tục cả cầu thang**, từ cổ bậc đầu tiên ở tầng thấp nhất (mẫu: vế tầng trệt 1…15, vế tiếp 16…30, vế tầng trên 31…45);
  - hiện **cách một số** (số lẻ 1, 3, 5…) cho đỡ dày;
  - V1 và V3 cùng dải → hai cột số ở **hai phía** của dải, không chồng nhau (mẫu: V3 sát tường, V1 sát lan can giữa);
  - số không đè mũi tên path, tag, nét cắt.
  - Số bắt đầu của mỗi vế = số cuối của vế trước + 1. Kiểm tra bằng ảnh; sai thì chỉnh số bắt đầu của tread number (làm tay nếu tool không chỉnh được).

## D. STAIR PATH

- Mỗi **thang** nhìn thấy có **một** stair path (V1+V2 cùng thang dưới → 1 path; V3 thang tầng này → 1 path).
- Path thể hiện chiều đi **từ dưới lên trên**: type thuộc family **Fixed Up Direction** (mũi tên luôn chỉ chiều đi lên).
- **Không** hiện chữ UP / DOWN: tắt Show Up Text và Show Down Text. **Chỉ có hình mũi tên.**
- Mẫu: mũi tên dạng **chữ V phủ hết bề rộng vế** (full step arrow) ở **đầu trên** của mỗi vế.
- Path không đè tag, số bậc, chữ cao độ.
- Không tạo type path mới nếu dự án đã có type Fixed Up Direction; chưa có → hỏi user.

## Mẫu tham chiếu (ảnh của user, 2026-10-06)

Sheet mặt bằng lõi thang bộ của user, view tầng điển hình (thấy đủ V1, V2, V3), tỉ lệ 1:50:

| Mục | Trên mẫu |
|---|---|
| V1 / V2 / V3 | tag `From EL +0 To EL +2625` · `From EL +2625 To EL +5250` · `From EL +5250 To EL +7875`, đều `280mm x 14T` |
| SA1 (hai đầu vế, trong lõi) | `70 \| 1550 CLEAR \| 80 \| 300 \| 80 \| 1550 CLEAR \| 70` |
| SA2 (ngoài tường bên, cả hai phía) | `2205 \| 280mm x 14T = 3920 (EQUAL TREADS) \| 1805 \| 400`, tổng `8330` |
| SA3 | `1965 CLEAR` trên chiếu nghỉ và trên chiếu tới, dọc trục giếng |
| SA4 (ngoài tường đầu) | `140 \| 3560`, tổng `3700` |
| SB2 / SB3 / SB5 | `P01`, `P02` · `2625`, `5250` · `F13` |

Chỗ mẫu khác với quy tắc viết → **làm theo quy tắc**:
- tổng tường–tường (`8330`, `3700`) chưa có `CLEAR` → thêm `CLEAR`;
- `280x13T= 3640` (một view khác trên cùng sheet) → sửa về `280mm x 13T = 3640`;
- tag `P01`/`P02` đè nét bậc → dời ra chỗ trống.

## Kiểm tra sau khi làm

- `stair_plan_audit` lại: mọi mục SA1–SD đều `OK` (hoặc `OK (split)` khi đoạn được chia nhỏ trên cùng một đường dim, vd. có trục chen giữa).
- `annotation_overlaps` cho view.
- Xuất ảnh sheet, nhìn: 3 vế, 3 tag vế ngoài tường có leader, số bậc liên tục đúng, mũi tên đúng chiều, không chữ UP/DOWN, các lớp dim thẳng hàng và đúng thứ tự.

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-06: `stair_plan_audit`, `stair_plan_annotate`, `dims_text` đã biên dịch với RevitAPI 2023 nhưng **chưa chạy thử trên model thật**. Lần đầu: chạy trên một view, đối chiếu kết quả với bản vẽ, ghi bài học.
- 2026-10-06: chuẩn A–D lấy từ user. Bố trí dim/tag, cách viết công thức, đánh số bậc liên tục và mũi tên chữ V lấy theo ảnh mẫu của user (sheet lõi thang, 1:50).
