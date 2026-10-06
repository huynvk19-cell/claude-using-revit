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
| Dim type kiểm tra (màu, dùng khi review) | `<vd. 2.0mm Arial Narrow - check>`. Tên trung tính, không chứa "Claude"/"AI". |
| Tiền tố số sheet | `<vd. DRW-XXX-A>` |
| Thư mục log / review | `log/YYYY-MM.md`, `review/<ngày>_<mã>/` |
| Family cửa cuốn (lấy kích thước danh nghĩa) | `ROLL UP` |
| Phía ưu tiên đặt dim trục (G2: mỗi nhóm một phía) | `<vd. dưới + trái>` → `preferSides:["bottom","left"]` |
| Trục trùng vị trí (tính là một) | `<vd. trục C trùng trục 12>` |
| View không dim trục | STAIRCASE, PLAN TOILET…, ELEVATION n-UT, `<thêm>` |
| View mặt bằng lõi thang bộ (`drafting-stair-core`) | `<vd. tên chứa STAIRCASE … PLAN>` |
| Lõi thang: hiện số bậc? / phía số bậc | `<có / không>` · `<left / right>` |
| Lõi thang: type tag/path riêng (nếu không dùng type phổ biến nhất) | `<vd. stair path: Fixed Up Direction : Arrow only>` |
| Sổ tay dự án (nếu có) | `<link>` |

## Ghi chú riêng của dự án

- <Mỗi dòng một bài học, kèm ngày, vd. "2026-10-05: view mezzanine dời xuống 30 mm theo comment">
