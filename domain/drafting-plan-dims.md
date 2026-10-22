---
name: drafting-plan-dims
description: "Chuẩn khai triển mặt bằng kích thước (floor plan dimension drawings): các bước và quy tắc riêng của mặt bằng. Hiện có: dim cửa tới tường và trục (PD1–PD6), tag cửa (PT1–PT6), tag room (TR1–TR8), tag cao độ sàn (FL1–FL3), tag tường (WT1–WT5), ký hiệu độ dốc (SL1–SL5). Floor plan dimension standard."
metadata:
  updated: "2026-10-17"
  related: ["drafting-annotation", "drafting-grid-dims", "drafting-opening-dims", "drafting-opening-tags", "drafting-stair-plan", "drafting-work-rules", "drafting-tools"]
---

# Khai triển mặt bằng kích thước (Floor plan dimensions)

Áp dụng cho mặt bằng các tầng đặt trên sheet. Không áp dụng cho mặt bằng lõi thang (`drafting-stair-plan.md`) và mặt bằng trần (`drafting-ceiling-plan.md`).

Skill: `drafting-plan-dims`; mỗi mục dưới đây là một thẻ bước của skill, chạy theo thứ tự thẻ.

| Thẻ | Mục | Quy tắc |
|---|---|---|
| 00 | Dim cửa đi, cửa sổ → tường, trục | PD1–PD6 (mục 1) |
| 01 | Tag cửa đi, cửa sổ | PT1–PT6 (mục 2) |
| 02 | Tag room | `drafting-annotation.md` §2 TR1–TR8: leader ngắn nhất, trong room nếu đủ chỗ (leader 0 = điểm tối đa, giảm về 0 khi leader dài ra), thẳng hàng +2; nằm trên tường / cạnh sàn, leader trùng leader / trùng tường / qua chấm leader khác → 0; leader qua room khác −3 mỗi room; thiếu tag → điểm thẻ 0. |
| 03 | Tag cao độ sàn | `drafting-annotation.md` §2b FL1–FL3: mỗi gian phòng một cao độ (trừ phòng nhỏ nằm trong phòng khác); buồng thang có cao độ bên trong và bên ngoài cửa; type theo người dùng. Cùng thẻ với `drafting-plan-floor-finish`. |
| 04 | Tag tường | WT1–WT5 (mục 3) |
| 05 | Ký hiệu độ dốc | `drafting-annotation.md` §2c SL1–SL5: mái, sàn chênh cao độ, ramp, mọi mặt dốc trên view có một Spot Slope; type có Slope Direction = Down, đơn vị số thập phân hoặc phần trăm, dùng type có sẵn thoả (không có thì hỏi). Cùng thẻ với `drafting-plan-floor-finish`. |

Thứ tự: dim trục (`drafting-grid-dims.md`, G1–G4) → dim cửa → tag cửa → tag room → cao độ sàn → tag tường → ký hiệu độ dốc. Dim khó dời hơn tag, nên dim đặt trước, tag tránh dim; tag room tránh mọi dim và tag cửa (`clearMm`, §2); cao độ sàn tránh tag room (cùng muốn tâm phòng, tag room được ưu tiên); tag tường và ký hiệu độ dốc đặt sau cùng, tránh mọi thứ đã đặt (tag tường chỉ cần một đoạn tường trống, ký hiệu độ dốc chỉ cần một chỗ trống trên mặt dốc).

Nguồn: PD lấy từ Q1–Q5 (`drafting-opening-dims.md`, user 2026-10-06) và SA4 của mặt bằng lõi thang (`drafting-stair-plan.md`, user 2026-10-07); PT lấy từ T1–T8 (`drafting-opening-tags.md`), SB4 và bài học đợt tag cửa mặt đứng/mặt cắt 2026-10-16. Mặt bằng thường chưa có mẫu riêng của user → lần đầu làm ở một dự án: làm xong **một view mẫu**, cho user duyệt, ghi điều user chỉnh vào profile, rồi mới làm các view còn lại.

## Phạm vi chung (cả hai thẻ cửa)

- Cửa "có trên view" = cửa đi / cửa sổ **host** mà mặt bằng **vẽ ra** (bị mặt cắt cắt qua hoặc nhìn thấy). Không tính: cửa của link, cửa bị che (dưới vùng tô / masking region, ngoài crop), cửa tầng khác chỉ hiện do view range.
- View OVERALL / key plan tỉ lệ nhỏ: **không** dim, **không** tag cửa (như T1).
- Vùng mà profile ghi là "chỉ dim trục" (vd. vùng hangar, vùng thiết bị) → không dim, không tag cửa ở đó.
- Chỉ thêm annotation. Không sửa cửa, tường, trục (R1). Không dời / xoá dim, tag của người khác (mục "Của ai" bên dưới).

### Của ai

Trước khi dời, đổi type hay xoá một tag / dim có sẵn: `annot_owners {viewId, listIds:true}`. Được sửa khi **LastChangedBy** = tài khoản Revit của người dùng (profile) hoặc phần tử do Claude tạo (type đánh dấu). Của người khác → để nguyên, ghi vào báo cáo, trừ khi user cho phép trong phiên đó (đợt 2026-10-16 user cho phép giữa chừng → dời cả tag người khác).

## 1. Dim cửa đi, cửa sổ (PD1–PD6) — thẻ 00

| # | Quy tắc |
|---|---|
| PD1 | **Mỗi cửa có trên view nằm trong một chuỗi dim ngang theo tường chứa nó**: đọc được bề rộng cửa và cửa cách trục / tường bao nhiêu. Mặt bằng **không** dim chiều cao cửa (chiều cao ở mặt đứng, mặt cắt, Q2). |
| PD2 | **Chuỗi tới trục**: trục → mép cửa → mép cửa → … → trục, dọc theo tường chứa cửa (như Q3). Trục host gần nhất mỗi bên trong khoảng ~12 m, đang thấy trên view; không trục link. |
| PD3 | **Chuỗi tới tường**: chuỗi đi qua tường giao (tường vuông góc cắt ngang chuỗi) thì lấy **hai mép tường hoàn thiện** của tường đó (thể hiện bề dày, như SA4: `trục → mép tường → mép tường → mép cửa → mép cửa → trục`). Một phía không có trục trong ~12 m → chuỗi dừng ở **mép tường hoàn thiện** gần nhất phía đó (góc tường, đầu tường), báo user. Cửa trong tường ngăn ngắn giữa hai tường giao → mép tường → mép cửa → mép cửa → mép tường là đủ. |
| PD4 | **Gộp chuỗi**: nhiều cửa cùng một tường giữa hai trục → **một** chuỗi chung. Cửa sát nhau (< 150 mm) → có điểm nối giữa các cửa (`drafting-opening-dims.md` §3). Chuỗi dim có sẵn chạy qua cửa mà thiếu mép cửa → thêm mép vào chính chuỗi đó (cùng đường, cùng type) nếu chuỗi là của mình; của người khác → vẽ chuỗi mới, báo. |
| PD5 | **Tham chiếu** (như Q5): cửa = tham chiếu của chính cửa (mép khung, `Left` / `Right`); cửa cuốn = bề rộng danh nghĩa `LEFT` → `RIGHT` (family theo profile). Tường = **mặt hoàn thiện** lấy từ 3D (`src:"3d"`). Trục = trục host. Không lấy mép lỗ tường, detail line, hình học link. |
| PD6 | **Vị trí**: tường bao → phía **ngoài nhà**, đường dim đầu tiên ngoài mặt tường (~7 mm giấy), **nằm trong** dải dim trục (dim trục luôn ngoài cùng, G3). Tường trong → phía không có cung mở cửa, không đè room, tag, chữ. Các đường dim song song cách nhau ~7 mm giấy. Chữ đoạn ngắn đè nhau → dời chữ (`dims_text_move`). Hết chỗ → không đẩy dim ra xa, báo user đặt tay. |

**Coi là đã có**: một chuỗi sẵn có (của ai cũng được) đã tham chiếu đúng hai mép cửa (PD5), cho đúng chuỗi (PD2/PD3) và nằm sát tường (PD6) → không thêm chuỗi thứ hai. Chuỗi đang trỏ vào mép lỗ tường → không tính; thêm chuỗi đúng, báo user chuỗi cũ có cần xoá không.

Type: dim type **kiểm tra** của profile (= type đánh dấu). Mỗi lần gọi `dims_at_positions` là một view; sau apply kiểm dim có vẽ ra không (`view_elem_boxes`: `Box` ≠ null).

## 2. Tag cửa đi, cửa sổ (PT1–PT6) — thẻ 01

| # | Quy tắc |
|---|---|
| PT1 | **Đủ và đúng**: mỗi cửa có trên view có **đúng một** tag. Cửa không vẽ ra → không tag (tag tool lỡ đặt cho cửa bị vùng tô che → xoá). Hai tag cùng một cửa (kể cả một tag type mark nhiều leader trùng với các tag số) → giữ tag đặt tốt hơn, xoá tag kia nếu là của mình; của người khác → báo. Tag mồ côi, tag "?" / rỗng → báo. |
| PT2 | **Sát cửa, đúng phía**: đầu tag đặt sát cửa (~1–2 mm giấy từ mặt tường), căn giữa bề rộng cửa. Cửa đi: phía **không có cung mở cửa**; mở cả hai phía → phía hành lang / phía ngoài phòng. Cửa sổ: phía **ngoài nhà**; ngoài nhà chật (dim, trục) → phía trong. Cửa rất rộng (cửa cuốn lớn, cửa hangar): phía trong nhà, giữa bề rộng cửa. |
| PT3 | **Không đè**: đầu tag không nằm trên khung cửa, lá cửa, cung mở cửa (`door_window_tag_check`: `overlap`), tường, cột, cầu thang, thiết bị; không đè dim (đường + chữ), tag khác, room tag, text. Nét trục, nét sàn, hatch là vật mềm, được đè. |
| PT4 | **Leader**: tag sát cửa thì không leader. Vướng → dời tag ra chỗ trống gần nhất, có leader đi thẳng hoặc **một góc vuông** so với khổ giấy, điểm cuối chạm cửa (`drafting-annotation.md` "Leader của tag"). Không leader xiên. Tag có leader mà đầu tag vẫn nằm trên cửa → sửa (hoặc báo nếu của người khác). |
| PT5 | **Thẳng hàng**: các cửa cùng một tường, cùng hàng → đầu tag cùng khoảng cách tới tường (cùng một đường). Tag dời ra xa gom thành cột / hàng thẳng, leader dẫn tới cửa. |
| PT6 | **Type và tôn trọng việc có sẵn**: type tag cửa đi / cửa sổ dùng **nhiều nhất** trong view (không có → nhiều nhất trong dự án; không có → hỏi). Không tạo type mới ngoài type đánh dấu. Tag mới mang type đánh dấu. Không "đặt lại toàn bộ" trên view user đã chỉnh tay: chỉ thêm tag thiếu và sửa tag sai của mình. |

Kiểm tra sau khi làm: `door_window_tag_check` không còn `untagged` (trừ cửa lý do ghi rõ), `duplicate`, `overlap` trên tag của mình; `tag_leaders_info` không có `D`; `annotation_overlaps` không có tag cửa đè dim / tag; ảnh xuất nhìn gọn.

## 3. Tag tường (WT1–WT5) — thẻ 04

Nguồn: user 2026-10-17 (4 quy tắc: tag có sẵn / hỏi; thẳng, sát tường, không đè tường; bỏ vùng vệ sinh; phải có leader). Cách chia "một tag mỗi đoạn tường" (WT2) và phía đặt là mặc định Claude chọn, chờ user xác nhận ở view mẫu đầu tiên.

| # | Quy tắc |
|---|---|
| WT1 | **Type có sẵn**: dùng type tag tường đã có trong dự án: type profile cố định → type dùng nhiều nhất trong view → nhiều nhất trong dự án. Dự án chưa có type tag tường nào (hoặc có hai type mà không rõ chọn cái nào) → **hỏi trực tiếp người dùng**, chờ trả lời. Không tự tải / tạo family tag tường. Tag mới mang type đánh dấu. |
| WT2 | **Đủ**: mỗi đoạn tường host mặt bằng vẽ ra có **một** tag; một đoạn = các tường cùng type nối thẳng hàng liên tục. Tường đã có tag tường (của ai cũng được) → coi là đã có. Không tag tường link, tường bị che, tường vùng "chỉ dim trục". |
| WT3 | **Thẳng, sát tường, không đè tường**: đầu tag đặt thẳng ra từ mặt tường, vuông góc với tường, hộp tag cách mặt tường ~2 mm giấy: sát nhưng **không chạm, không đè** nét tường. Đặt ở đoạn mặt tường trống (không trên cửa, không dưới dim, tag, chữ); tường bao → phía trong nhà (ngoài nhà là dải dim), tường trong → phía phòng còn chỗ. Không đè dim, tag cửa, room tag, cao độ, text (như PT3). |
| WT4 | **Bỏ vùng vệ sinh**: tường bao quanh / nằm trong phòng vệ sinh (tên room chứa phần tên theo profile, mặc định `TOILET`, `WC`) **không** tag: đã thể hiện ở bộ mặt bằng vệ sinh. Tường dài chỉ một phần giáp vệ sinh → tag ở phần ngoài vùng vệ sinh. |
| WT5 | **Phải có leader**: mọi tag tường có leader, điểm cuối chạm mặt tường; leader ngắn, **một đoạn thẳng** vuông góc với tường (`tag_leaders_info`: `H` hoặc `V`), không gấp khúc, không xiên. |

Kiểm tra sau khi làm: mỗi đoạn tường ngoài vùng vệ sinh có một tag (hoặc ghi lý do); `view_elem_boxes`: hộp tag không cắt nét tường; `tag_leaders_info`: tag tường nào cũng có leader `H` / `V`; `annotation_overlaps` sạch; ảnh xuất nhìn gọn.

## Việc tồn của chuẩn

- Mặt bằng chưa có lệnh khảo sát cửa riêng (vị trí mép cửa theo khung view, cửa nào đã có dim / tag): thẻ 00, 01 dùng `query_elements_with_filter` + `view_elem_boxes` + `door_window_tag_check`. Làm nhiều lần thấy chậm → viết lệnh `plan_openings_survey`.
- Tag tường (thẻ 04) chưa có lệnh khảo sát riêng (đoạn tường nào chưa tag, đoạn mặt tường trống, tường giáp phòng vệ sinh): dùng `query_elements_with_filter` + `view_elem_boxes` + `rooms_dump` / `room_at_point`. Làm nhiều lần thấy chậm → viết lệnh `wall_tags_missing` (như `room_tags_missing`).
- Các mục khác của mặt bằng kích thước (dim bề dày tường…) sẽ thêm thành thẻ mới khi có quy tắc.
