---
name: drafting-profile
description: "Giá trị riêng của dự án cho bộ skill drafting. Copy file này vào thư mục gốc của dự án, đặt tên drafting-profile.md, rồi điền. Project-specific values."
---

# Drafting profile — <TÊN DỰ ÁN>

| Mục | Giá trị |
|---|---|
| Phiên bản Revit | 2023 |
| Model **không được chạm** | `<tên model>` |
| Dim type chính thức | `<vd. 2.0mm Arial Narrow>` |
| Hậu tố type đánh dấu (mọi phần tử tạo mới, `mark_types`) | `<vd.  - check>` (có dấu cách đầu). Trung tính, không chứa "Claude"/"AI". Type đánh dấu = `<type chính thức><hậu tố>` |
| Dim type kiểm tra | = dim type chính thức + hậu tố, vd. `2.0mm Arial Narrow - check` |
| Màu type đánh dấu (dim, text) | `<vd. [0,0,255]>` |
| Tiền tố số sheet | `<vd. DRW-XXX-A>` |
| Thư mục log / review | `log/YYYY-MM.md`, `review/<ngày>_<mã>/` |
| Family cửa cuốn (lấy kích thước danh nghĩa) | `ROLL UP` |
| Phía ưu tiên đặt dim trục (G2: mỗi nhóm một phía) | `<vd. dưới + trái>` → `preferSides:["bottom","left"]` |
| Trục trùng vị trí (tính là một) | `<vd. trục C trùng trục 12>` |
| View không dim trục | STAIRCASE, PLAN TOILET…, ELEVATION n-UT, `<thêm>` |
| View mặt bằng lõi thang bộ (`drafting-stair-plan`) | `<vd. tên chứa STAIRCASE … PLAN>` |
| View mặt cắt dọc thang (`drafting-stair-section-parallel`) | `<vd. tên chứa STAIRCASE … SECTION>` |
| Mặt cắt thang: dim thông thuỷ dưới chiếu nghỉ (LA4) có `CLEAR`? | `<không (theo mẫu) / có>` |
| Lõi thang: hiện số bậc? / phía số bậc | `<có / không>` · `<left / right>` |
| Lõi thang: type tag/path riêng (nếu không dùng type phổ biến nhất) | `<vd. stair path: Fixed Up Direction : Arrow only>` |
| Room tag: type cố định của dự án (để trống = hỏi user mỗi việc) · kích thước tag trên giấy (rộng × cao mm) | `<tên type, không kèm tên family>` · `<vd. 24 x 8>` → `typeName`, `tagSizeMm:[24,8]` |
| Tài khoản Revit của người dùng (annotation của mình = LastChangedBy, `annot_owners`) | `<tên đăng nhập Revit>` |
| Mặt bằng: vùng chỉ dim trục (không dim / tag cửa, `drafting-plan-dims` 00–01) | `<vd. vùng xưởng, x > 61 m>` |
| Cao độ sàn (FL1–FL3): type cao độ (để trống = user chọn) · ngưỡng phòng nhỏ trong phòng khác (m²) · tên room buồng thang chứa · tên view lõi thang chứa · ký hiệu cao độ hiện trạng ngoài nhà | `<vd. Plan>` · `5` → `minAreaM2` · `STAIR` → `stairRoomText` · `STAIRCASE` → `stairViewText` · `<id generic annotation, không có thì bỏ>` → `civilSymbolId` |
| Tag tường (WT1, WT4, `drafting-plan-dims` 04): type tag tường cố định (để trống = type đang dùng, chưa có thì hỏi) · phần tên room vệ sinh (không tag tường) | `<tên type, không kèm tên family>` · `TOILET, WC` |
| Fill kết cấu (SF1–SF10, `drafting-section-general` 00): model kết cấu (`sources`) · type bê tông · type đất · line style nét 8 · level tầng trệt · regex tên đài cọc, cọc | `<tên model>` · `<type>` · `<type>` · `<line style>` · `<level>` · `<regex>` |
| Ký hiệu độ dốc (SL2–SL4, `drafting-plan-dims` 05, `drafting-plan-floor-finish` 01): Spot Slope type cố định (để trống = type có sẵn hướng Down, đơn vị thập phân / %) | `<tên type>` |
| Schedule danh mục bản vẽ / print set khớp với nó | `<vd. DRAWINGS LIST>` / `<tên print set>` |
| Layout: loại công trình (+ hỗn hợp, loại con) · giai đoạn (`drafting-sheet-layout` 00) | `<nhà liên kế / biệt thự / nhà cao tầng / sân bay / công cộng: trường học, bệnh viện, TTTM / công nghiệp>` · `<YT / CS / KT / TC / HC>` |
| Layout: khổ giấy · khung tên · vùng vẽ (lề, dải khung tên, cột ghi chú, mm) · khoảng hở giữa viewport (mm) | `<vd. A1>` · `<tên type khung tên>` · `<vd. lề 10, khung tên phải 180, ghi chú 120>` · `15` |
| Layout: định dạng số sheet theo nhóm · mã mẫu nhà / hạng mục / khối | `<vd. tiền tố + nhóm + 2 chữ số>` · `<vd. mẫu A, B, góc>` |
| Layout: vùng (scope box) · thứ tự sheet vùng · ngôn ngữ tên sheet | `<tên scope box>` · `<theo tầng rồi vùng / theo vùng rồi tầng>` · `<Việt / Anh / song ngữ>` |
| Sheet (`drafting-sheet-create`): tham số chung + giá trị (giai đoạn, ngày phát hành, người vẽ / kiểm / duyệt) · tham số nhóm Project Browser + giá trị theo nhóm | `<tên tham số = giá trị>` · `<tên tham số>` |
| View (`drafting-view-create`): template theo khoá `plan`, `plan-finish`, `ceiling`, `roof`, `site`, `site-setout`, `elevation`, `section`, `stair`, `toilet`, `detail`, `legend` · mẫu tên view · tham số nhóm view | `<khoá = tên template>` · `<vd. = tên bản vẽ, in hoa>` · `<tên tham số>` |
| Đặt view (`drafting-sheet-place`): viewport type theo loại view · trục gốc căn chuỗi mặt bằng · vị trí ghi chú / key plan / mũi tên Bắc | `<loại = type>` · `<vd. 1 + A>` · `<vd. cột phải, góc dưới phải>` |
| Sổ tay dự án (nếu có) | `<link>` |

## Ghi chú riêng của dự án

- <Mỗi dòng một bài học, kèm ngày, vd. "2026-10-05: view mezzanine dời xuống 30 mm theo comment">
