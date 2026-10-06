---
name: drafting-grid-dims
description: "Chuẩn dim trục (grid dims) trên bản vẽ kiến trúc: nhóm trục song song, mỗi nhóm đúng 1 dim cách trục + 1 dim tổng trên mỗi bản vẽ, đặt trong dải giữa mép crop và bubble, annotation crop kéo ra tới bubble. Grid dimension standard G1–G4."
metadata:
  updated: "2026-10-06"
  related: ["drafting-dimensions", "drafting-views-sheets", "drafting-tools", "drafting-api-pitfalls", "drafting-work-rules"]
---

# Dim trục (Grid dimensions)

Áp dụng cho mọi bản vẽ kiến trúc đặt trên sheet có trục: mặt bằng, mặt đứng, mặt cắt.
Không áp dụng cho các view loại trừ trong `drafting-profile.md` (vd. STAIRCASE, toilet/utility, dải partial hẹp).

Tool: `grid_dims_band` (audit / preview / apply / undo). Skill: `drafting-grid-dims`.

## Bốn quy tắc (G1–G4)

| # | Quy tắc |
|---|---|
| **G1** | Các trục **song song** với nhau tạo thành **một nhóm**. Mỗi nhóm cần **dim cách trục** (chain) và **dim tổng trục** (overall) trên **mỗi bản vẽ kiến trúc**. |
| **G2** | Trên mỗi bản vẽ, mỗi nhóm chỉ có **đúng 1** dim cách trục và **đúng 1** dim tổng trục. |
| **G3** | Dim trục nằm **ngoài crop boundary** và **trong bubble** của trục, tức là trong dải giữa mép crop và đầu trục có bubble. |
| **G4** | Dim trục không được mất vì nằm ngoài annotation crop: **luôn kéo annotation crop ra tới bubble** của trục. |

## G1 — Nhóm trục

- **Nhóm** = các trục thẳng của model (host) đang hiện trong view và có cùng phương. Góc nào cũng được: dọc, ngang, xiên.
- Trục **trùng vị trí** (cách nhau < 5 mm, khai báo trong profile, vd. trục C trùng trục 12) tính là **một** trục. Dim tham chiếu bất kỳ trục nào trong số đó đều được.
- Nhóm chỉ có **1 trục** trong view → không cần dim.
- Trục cong (arc) không thuộc nhóm nào → báo để làm tay.
- **Chain** = dim tham chiếu **mọi** trục của nhóm đang hiện trong view.
- **Overall** = dim chỉ có 2 tham chiếu: trục đầu và trục cuối của nhóm.
- Nhóm **2 trục**: chain và overall trùng nhau → **một dim là đủ**.
- Dependent view: chain của view cha có thể nối dài qua cả những trục không hiện trong view này. Nếu nó phủ đủ các trục đang hiện thì vẫn tính là chain của view này.

## G2 — Đúng 1 chain + 1 overall, cùng một phía

- Hai dim giữ lại nằm **cùng một phía** của nhóm. Không dim cả hai phía.
- Các dim trục khác của nhóm là **dim thừa**:
  - chain/overall trùng lặp;
  - chain thiếu trục (partial);
  - dim tổng phủ rộng hơn view;
  - dim trỏ vào **trục của link** (dim lại bằng trục host).
- Chọn dim để giữ, theo thứ tự ưu tiên:
  1. dim nằm đúng dải ở phía được chọn;
  2. dim ở phía được chọn;
  3. type chính thức đứng trước type kiểm tra.
- Xử lý dim thừa (chỉ khi user duyệt):
  - dim chỉ thuộc view này → **xoá**, có log để dựng lại;
  - dim của view cha mà một view khác trên sheet đang dùng → **chỉ ẩn ở view này**.

## G3 — Vị trí: dải giữa crop và bubble

Thứ tự từ ngoài vào trong (số đo là mm trên giấy):

```text
 (O)  (O)  (O)        <- bubble (đầu trục)
  |    |    |           4 mm
  |<-- overall -->|
  |    |    |           7 mm
  |<-->|<-->|           chain
  |    |    |           >= 1.5 mm (cộng chiều cao chữ nếu chữ quay về phía crop)
+-----------------+   <- mép crop (model crop)
|   công trình    |
```

- **Dải cần có**, tính bằng mm giấy:
  - nhóm nhiều trục: 4 + 7 + 1.5 (+ ~2.8 khi chữ quay vào crop) ≈ **12.5–14.8**;
  - nhóm 2 trục: ≈ **5.5–7.8**.
- **Chọn phía**, theo thứ tự ưu tiên:
  1. phía đã có dim đúng dải;
  2. phía có bubble;
  3. phía ưu tiên trong profile (`preferSides`, vd. dưới + trái);
  4. phía rộng hơn.
- Phía không có bubble chỉ dùng khi phía có bubble quá hẹp. Khi đó phải báo cho user.
- **Chữ của dim tổng** đè lên đường trục → dời chữ dọc theo dim sao cho mép chữ cách trục 0.5–1 mm.
- **Dải quá hẹp hoặc âm** (đầu trục nằm **trong** crop): không tự sửa, báo user chọn một trong hai cách:
  - (a) **Kéo đầu trục 2D** ở phía đó ra ngoài crop cho đủ dải (`extendGrids`). Chỉ đổi 2D extent của view đó, có log để undo.
  - (b) **Thu crop** vào sát công trình. Làm tay. View gắn Scope Box thì crop bị khoá → phải chỉnh scope box.

## G4 — Annotation crop

- Revit **không cắt trục và bubble** theo annotation crop, nhưng **có cắt dim**. Dim nằm ngoài crop mà annotation crop chưa nới ra thì không hiện, hoặc hiện mất một phần.
- Annotation crop phải **tới được mọi đầu trục có bubble**, cộng 2 mm giấy, và chứa trọn mọi dim trục.
- Chỉ **nới ra**, không thu vào.
- Annotation crop **đang tắt**: Revit không cắt gì, nên giữ nguyên. Chỉ bật khi user yêu cầu (`activateAnnoCrop`).
- Nới annotation crop làm khung viewport trên sheet to ra. Sau đó kiểm tra chồng lấn với view khác hoặc khung tên, rồi căn lại title (`drafting-tags-titles`).

## Dependent view

- Dim thuộc **view cha**. Dim hiện ở mọi view con có annotation crop chứa nó.
- Dim **mới hoặc vừa dời** mà lộ ra ở một view anh em đang đặt trên sheet → ẩn ở view đó (`hideInSiblings`, có log).
- Dim **dùng chung**: nếu trước đó nó đã hiện ở view anh em (vd. chain chạy suốt hai nửa mặt bằng, hiện ở cả hai dependent) thì không ẩn. Dời dim này ảnh hưởng cả view kia → **chạy lại view kia**.
- Kéo đầu trục 2D trong một view con: kiểm tra lại các view anh em sau khi làm.
- Chain của view cha đã được view kia dời ra **đúng vị trí dải** của view này (do cùng mép crop) → lệnh **nhận lại** chain đó và nới annotation crop cho nó hiện ra, không tạo chain trùng.
- Hai view con có **mép crop khác nhau** thì không thể dùng chung một chain: một vị trí không đúng dải cho cả hai. Giữ chain chung cho một view; ở view kia đánh dấu nó là dim thừa (`extraIds`) để ẩn ở đó, rồi tạo chain riêng.

## Sau khi kéo đầu trục

- **Title của view** thường nằm giữa công trình và hàng bubble mới, và các đường trục chạy xuyên qua chữ. Dời title xuống dưới hàng bubble bằng `viewport_titles_place {ignoreCrop:true, onlyViewports}`.
- **Kiểm tra cả sheet bằng ảnh.** Bubble và dim vừa kéo ra không được đè lên viewport khác cùng sheet và không được ra ngoài khung giấy.
  - Đè view khác → undo view đó, rồi chọn phía khác bằng `forceSides` (phía không có bubble cũng được, nếu còn dải). Ghi lại trong "Cần xem".
  - Ra ngoài giấy và không còn phía nào khác → bỏ qua, ghi vào "Việc tồn" (thu crop bằng tay).
- Title quá sát mép dưới khung giấy (< 15 mm) → báo trong "Cần xem".

## View không cần (hoặc không thể) dim trục

- **Key plan** (sơ đồ chỉ dẫn: thang thoát hiểm, tiện ích…): trục chỉ để định vị. Loại theo tên trong profile.
- View có template **ẩn category Dimensions**: dim tạo ra sẽ không bao giờ hiện. `grid_dims_band` tự bỏ qua (`DimensionsHidden`).

## Thông số mặc định (`grid_dims_band`)

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `overallMm` | 4 | đầu trục → dim tổng (mm giấy) |
| `stepMm` | 7 | dim tổng → dim cách trục |
| `cropGapMm` | 1.5 | khe hở tối thiểu giữa dim (kể cả chữ) và mép crop |
| `annoMarginMm` | 2 | annotation crop vượt quá đầu trục có bubble / dim |
| `preferSides` | — | lấy từ profile |
| `move` | true | dời dim đang giữ vào dải |
| `deleteExtra` | false | xoá/ẩn dim thừa — **cần user duyệt** |
| `extendGrids` | false | kéo đầu trục 2D — **cần user duyệt** |
| `fitAnnoCrop` | true | nới annotation crop |
| `hideInSiblings` | true | ẩn dim mới ở view anh em |
| `forceSides` | — | ép phía cho từng loại nhóm, vd. `{horizontal:"right"}` — quyết định của user |
| `extraIds` | — | coi các dim chỉ định là dim thừa (vd. chain chung của hai view con có crop khác nhau) |
| `compact` | false | kết quả gọn (hành động + trạng thái sau) khi chạy hàng loạt |

Dim mới dùng **type kiểm tra** trong profile. Đổi sang type chính thức bằng `swap_dim_type` sau khi user duyệt.

## Bài học (ghi thêm một dòng, kèm ngày)

- 2026-10-06: offset annotation crop trong API tính bằng **feet trên giấy** (0.0164 ft = 5 mm), không nhân tỉ lệ view. Đã sửa `view_crop_info`.
- 2026-10-06: dự án mẫu cho thấy đầu trục của nhiều mặt bằng nằm **trong** crop (dải −35 đến −104 mm). Muốn đạt G3 thì phải kéo đầu trục hoặc thu crop, nên luôn hỏi user trước.
- 2026-10-06: kéo trục 100+ mm thì title nằm kẹt giữa công trình và bubble. User chọn giữ cách kéo trục và dời title xuống dưới bubble.
- 2026-10-06: phía trái cần kéo 182 mm đã đè lên view mezzanine cùng sheet → đặt dim ở phía phải (không có bubble, còn dải 25 mm).
- 2026-10-06: Revit không trả bounding box cho dim nằm ngoài annotation crop, nên lệnh tính khung dim từ đường dim và chiều cao chữ.
