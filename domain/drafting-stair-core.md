---
name: drafting-stair-core
description: "Chuẩn triển khai chi tiết mặt bằng lõi thang bộ: dim thông thuỷ vế thang (CLEAR), dim chiều dài vế thang kèm công thức (280mm x 14T = 3920), dim chiếu nghỉ, dim tường/cửa tới trục; tag vế thang, tay vịn, cao độ, cửa, hoàn thiện sàn và tường; đếm/đánh số bậc từng vế; stair path chỉ có mũi tên. Stair core plan detailing standard."
metadata:
  updated: "2026-10-06"
  related: ["drafting-grid-dims", "drafting-opening-tags", "drafting-annotation", "drafting-tools", "drafting-api-pitfalls", "drafting-work-rules"]
---

# Mặt bằng lõi thang bộ (Stair core plan)

Áp dụng cho **mặt bằng chi tiết lõi thang bộ** (view tên thường có STAIRCASE / STAIR CORE, tỉ lệ 1:50 hoặc 1:25).
Không áp dụng cho key plan thang thoát hiểm (sơ đồ chỉ dẫn), mặt cắt thang.

Tool: `stair_core_audit` (read-only), `stair_core_annotate` (path, số bậc, tag vế), `dims_text` (CLEAR, công thức). Skill: `drafting-stair-core`.

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
- **Không đếm vế bằng mắt.** Xác định V1/V2/V3 bằng `stair_core_audit`: so cao độ vế với mặt cắt của view và độ chồng lên nhau của footprint.

### Hai phương

- **Hướng đi** (along): dọc theo vế thang.
- **Phương ngang** (across): vuông góc hướng đi, là phương đo bề rộng vế.
- **Tường song song hướng đi** = tường hai bên. **Tường vuông góc hướng đi** = tường hai đầu (phía chiếu nghỉ và phía sàn tầng / cửa).

### Mép hoàn thiện

- **Mép tường hoàn thiện** = mặt tường **gần lòng thang nhất**, đã gồm lớp hoàn thiện (lớp trát, ốp). Nếu hoàn thiện vẽ bằng tường riêng thì lấy mặt của tường hoàn thiện đó.
- **Mép trong tay vịn** = mép tay vịn quay về **phía lòng vế thang**.

## A. DIM

**Mọi kích thước thông thuỷ đều có suffix `CLEAR`** (đặt ở Suffix của từng đoạn dim, không Replace with text): SA1 bề rộng vế, SA1 tường–tường, SA2 tường–tường, SA3 chiếu nghỉ.

### Lớp dim (từ trong ra ngoài)

```text
 lòng thang | tường | 7 mm  lớp 1: chuỗi vế thang (SA1 hoặc SA2)
                    | 7 mm  lớp 1: tổng thông thuỷ tường–tường (CLEAR)
                    | 7 mm  lớp 2: tường, cửa → trục (SA4)
```

- Khoảng cách tính bằng **mm giấy**, đo từ **mặt ngoài cùng** của tường bao lõi thang.
- **Lớp 1** (dim vế thang, SA1–SA2) nằm **ngoài tường bao**. **Lớp 2** (SA4) nằm **ngoài lớp 1**.
- SA3 (chiếu nghỉ) đặt **trong lòng thang**, trên chiếu nghỉ.
- Mỗi phía của lõi thang chỉ có một bộ lớp. Không dim cùng một kích thước ở hai phía.

### SA1 — Bề rộng thông thuỷ vế thang (phương ngang)

- **Bề rộng thông thuỷ** một vế = khoảng cách giữa **mép trong hai tay vịn** của vế đó.
  - Phía không có tay vịn → tính tới **mép tường hoàn thiện**.
- **Chuỗi** (lớp 1, đo theo phương ngang), ví dụ lõi chữ U có lan can giữa:

  `tường HT → mép trong tay vịn tường → [vế 1 CLEAR] → mép trong tay vịn giữa → [lan can + giếng thang] → mép trong tay vịn giữa → [vế 2 CLEAR] → mép trong tay vịn tường → tường HT`

  - Đoạn tường HT → mép trong tay vịn = phần tay vịn tường nhô ra. Không có tay vịn tường thì không có đoạn này.
  - Đoạn giữa hai vế = **bề rộng lan can** (gồm cả giếng thang nếu có).
- **Tổng** (lớp 1, dòng ngoài): **tường HT → tường HT** giữa hai tường **song song hướng đi** + `CLEAR`.
- **Vị trí mặc định**: ngoài **tường đầu phía chiếu nghỉ** (tường vuông góc hướng đi, thường kín, ít cửa). Phía đó bị vướng (cửa, view khác) → phía đầu còn lại. *(Mặc định do Claude đề xuất, 2026-10-06 — user chỉnh thì ghi bài học.)*

### SA2 — Chiều dài vế thang (hướng đi)

- **Chiều dài vế** = **số bậc × độ sâu mặt bậc**.
- Đoạn dim chiều dài vế **có công thức** ở Prefix: `280mm x 14T = ` → hiện `280mm x 14T = 3920`.
  - 280 = độ sâu mặt bậc (mm), 14T = số bậc (T = tread), 3920 = giá trị dim đo được.
  - Số bậc lấy từ **model** (mục C), không đếm nét.
  - Giá trị dim phải **bằng** số bậc × độ sâu (lệch ≤ 1 mm). Lệch → dim đang bám sai mép → sửa tham chiếu, không sửa chữ.
- **Chuỗi** (lớp 1, đo theo hướng đi): `tường HT đầu → [sàn/chiếu tới] → mép bậc đầu → [vế: công thức] → mép bậc cuối → [chiếu nghỉ, tới mép tường HT] → tường HT đầu kia`.
  - Phải có đoạn **bề rộng chiếu nghỉ tới mép tường**.
- **Tổng** (lớp 1, dòng ngoài): **tường HT → tường HT** giữa hai tường **vuông góc hướng đi** + `CLEAR`.
- **Vị trí**: ngoài **tường bên** (song song hướng đi). Mỗi dải vế dim ở **phía tường gần nó**:
  - lõi chữ U: dải V1/V3 dim ở tường bên phía nó, dải V2 dim ở tường bên phía còn lại.
- **Dải V1/V3** chỉ có **một** dim chiều dài:
  - V1 và V3 cùng số bậc, cùng độ sâu, cùng mép đầu/cuối (± 5 mm) → một dim, một công thức.
  - Khác nhau → dim theo **V3** (vế của tầng này), báo V1 trong "Cần xem".

### SA3 — Chiếu nghỉ

- Ngoài các đoạn chiếu nghỉ đã có trong SA2, mỗi **chiếu nghỉ nhìn thấy** cần thêm **bề rộng thông thuỷ** + `CLEAR`.
- Đo từ **mép trong tay vịn** (hoặc **mép tường hoàn thiện**) tới **mép tường hoàn thiện** (hoặc **mép trong tay vịn**) phía bên kia, theo hướng đi.
  - Lan can giữa vòng qua chiếu nghỉ → đo từ mép tay vịn phía chiếu nghỉ tới tường HT.
- Đặt **trong lòng thang**, trên chiếu nghỉ, không cắt qua mũi tên stair path, tag và chữ cao độ.

### SA4 — Tường bao, cửa đi, cửa sổ → trục

- Chuỗi từ **trục** → **hai mép tường hoàn thiện** của từng tường bao (thể hiện bề dày tường) → **hai mép** cửa đi / cửa sổ → … → **trục**.
- Chỉ trục **đang thấy** trên view. Trục host, không dùng trục link.
- Cửa: tham chiếu vào chính cửa (như Q5 của `drafting-opening-dims.md`).
- **Lớp 2**, ngoài lớp dim vế thang.
- Phía có tường mà không có trục nào → chuỗi dừng ở mép tường cuối, báo user.

### Quy định chung cho dim

- Type: **type kiểm tra** của profile cho tới khi user duyệt.
- Đoạn dim ngắn hơn chữ (vd. tay vịn nhô 50 mm): để Revit đẩy chữ ra ngoài, hoặc dời chữ; chữ không đè chữ.
- Không đè lên tag, chữ cao độ, mũi tên stair path, số bậc.

## B. TAG

**Type tag lấy chung của dự án**: loại tag của category đó được dùng **nhiều nhất trong dự án** (`stair_core_audit` liệt kê). Không tạo type mới. Category không có tag nào trong dự án → hỏi user.

| # | Đối tượng | Số lượng | Vị trí |
|---|---|---|---|
| **SB1** | Vế thang (Stair Run Tag) | 1 tag / vế nhìn thấy (thường 3: V1, V2, V3) | **Trong phần nhìn thấy** của vế, gần giữa, không leader. Không bị dim đè, không nằm trên mũi tên path, số bậc, nét cắt. |
| **SB2** | Tay vịn / lan can (Railing Tag) | 1 tag / lan can | **Có leader** ngắn tới lan can. Đầu tag **không nằm trên nét vế thang**: đặt ở giếng thang, chiếu nghỉ hoặc khoảng trống cạnh lan can. |
| **SB3** | Cao độ (Spot Elevation) | 1 / chiếu nghỉ nhìn thấy; 1 sàn **ngoài cửa buồng thang** | Trên mặt chiếu nghỉ / mặt sàn hành lang ngay ngoài cửa. Không trên cung mở cửa, không trên mũi tên path. |
| **SB4** | Cửa đi, cửa sổ (Door/Window Tag) | đúng 1 / cửa nhìn thấy | Sát cửa, không đè dim (SA4), không nằm trên cung mở cửa. |
| **SB5** | Hoàn thiện sàn chiếu nghỉ | 1 / chiếu nghỉ | Trên chiếu nghỉ, gần cao độ SB3 (có thể cùng cụm), không đè path. |
| **SB6** | Hoàn thiện tường bao lõi thang | ≥ 1 / loại hoàn thiện tường | **Leader gần chủ thể nhất có thể.** Đầu tag có thể **dồn vào góc tường** (góc trong lõi thang còn trống). |

- Tag nào cũng phải: gần chủ thể, không đè dim, không đè tag khác, không nằm trên nét vế thang (trừ SB1 nằm trong vế của nó).
- Mỗi chủ thể **đúng một** tag. Tag trùng → xoá bớt (giữ tag đặt tốt hơn).
- SB5/SB6 dùng loại tag mà dự án đang dùng cho hoàn thiện (material tag, keynote hoặc tag sàn/tường hoàn thiện). Dự án chưa có thống nhất → hỏi, ghi vào profile.

## C. ĐẾM SỐ BẬC

- **Nguồn số liệu duy nhất là model**: số bậc (Actual Number of Treads) của **từng vế** (Stair Run), độ sâu mặt bậc (Actual Tread Depth) của thang chứa vế đó.
- **Không** đếm nét trên mặt bằng: V1 và V3 chỉ hiện một nửa, nét cắt che bậc.
- Đếm **riêng từng vế** V1, V2, V3. V1/V3 thuộc hai thang khác nhau → có thể khác số bậc (tầng trệt cao hơn…).
- **Kiểm tra chéo**: số bậc × độ sâu = chiều dài vế đo trên footprint (± 1 mm). Lệch → báo user, không tự sửa model.
- **Đánh số bậc** (Stair Tread/Riser Number) — mặc định **có**, trừ khi profile ghi không hiện:
  - đánh số **bậc** (tread), mỗi vế bắt đầu từ 1 ở bậc thấp nhất, số cuối = số T trong công thức SA2;
  - vế hiện một nửa: số trên phần nhìn thấy **tiếp theo đánh số của chính vế đó** (vd. V1 hiện 8…14, không đánh lại 1…7);
  - số không đè mũi tên path, tag SB1, nét cắt.

## D. STAIR PATH

- Mỗi **thang** nhìn thấy có **một** stair path (V1+V2 cùng thang dưới → 1 path; V3 thang tầng này → 1 path).
- Path thể hiện chiều đi **từ dưới lên trên**: dùng type thuộc family **Fixed Up Direction** (mũi tên luôn chỉ chiều đi lên).
- **Không** hiện chữ UP / DOWN: tắt Show Up Text và Show Down Text. **Chỉ có hình mũi tên.**
- Path không đè tag SB1, số bậc, chữ cao độ.
- Không tạo type path mới nếu dự án đã có type Fixed Up Direction; chưa có → hỏi user.

## Kiểm tra sau khi làm

- `stair_core_audit` lại: mọi mục SA1–SD đều `OK`.
- `annotation_overlaps` cho view.
- Xuất ảnh sheet, nhìn: 3 vế, 3 tag vế, số bậc đúng từng vế, mũi tên đúng chiều, không chữ UP/DOWN, các lớp dim thẳng hàng và đúng thứ tự.

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-06: `stair_core_audit`, `stair_core_annotate`, `dims_text` đã biên dịch với RevitAPI 2023 nhưng **chưa chạy thử trên model thật**. Lần đầu: chạy trên một view, đối chiếu kết quả với bản vẽ, ghi bài học.
- 2026-10-06: chuẩn A–D lấy từ user. Vị trí SA1 (phía tường đầu chiếu nghỉ), khoảng cách lớp 7 mm và đánh số bậc mặc định là đề xuất, chờ user xác nhận trên dự án đầu tiên.
