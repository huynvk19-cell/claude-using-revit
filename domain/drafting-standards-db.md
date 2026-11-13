---
name: drafting-standards-db
description: "Database chuẩn hồ sơ (SQLite): danh mục bản vẽ theo loại công trình tra được theo giai đoạn, điều kiện (thang máy, hầm, hồ bơi…), loại bản vẽ, mức; nhật ký các lần user sửa (ngày, phạm vi, quy tắc, đã vào chuẩn nào). Standards database: sheet catalogue queries, corrections log."
metadata:
  updated: "2026-11-13"
  related: ["drafting-sheet-layout", "drafting-coverage"]
---

# Database chuẩn hồ sơ (Standards database)

Skill: `drafting-standards-db`. Công cụ: `tools/standards-db/standards_db.py` (Python 3, không cần cài gì thêm); trên máy đã cài: `~/.claude/drafting-domain/tools/standards-db/`.

Database trả lời hai loại câu hỏi mà file `.md` trả lời chậm:
- **Tra chéo danh mục**: "biệt thự giai đoạn KT, có thang máy, không hồ bơi cần những bản vẽ nào", "loại công trình nào có bản vẽ `facade`", "những dòng nào còn ở mức 1".
- **Nhật ký sửa**: user đã sửa gì, ngày nào, cho loại công trình nào, quy tắc đó đã vào chuẩn nào chưa.

## DB1. File văn bản là nguồn, database là bản dựng

- Chuẩn `.md` vẫn là nguồn. Database **không thay** chúng và không có gì được sửa thẳng trong file `.db`.
- Mỗi lần gọi, công cụ dựng lại database trong bộ nhớ từ các file văn bản, nên kết quả không bao giờ cũ hơn chuẩn. `build` ghi ra file `standards.db` để mở bằng phần mềm xem SQLite (vd. DB Browser for SQLite). File đó không commit vào repo; CI dựng nó ở mỗi lần push và đăng cạnh bản cài, ở release `setup-latest`: `https://github.com/<owner>/<repo>/releases/download/setup-latest/standards.db`.

| Bảng | Dựng từ | Sửa ở đâu |
|---|---|---|
| `building_type`, `catalog_row`, `catalog_row_kind` | bảng bản vẽ của `drafting-sheet-layout.md` §4 và các file `drafting-sheet-set-<loại>.md` | chính các file `.md` đó |
| `kind` | `drafting-coverage.md` §2 | file đó |
| `stage` | `schema.sql` (YT / CS / KT / TC / HC) | — |
| `condition`, `catalog_row_condition` | `data/condition-keys.csv`, `data/condition-map.csv` | hai file CSV (DB3) |
| `correction` | `data/corrections.csv` | lệnh `correction-add`, `correction-set` (DB2) |

Hai view gộp sẵn: `v_catalog` (mỗi dòng bản vẽ + các `Loại` + mức thấp nhất của chúng) và `v_set` (bộ hồ sơ của một loại công trình: dòng của loại đó, cộng bộ lõi khi loại đó không có danh mục điển hình). Cột của từng bảng: `schema.sql`.

## DB2. Nhật ký sửa (corrections log)

- **Mỗi lần user sửa kết quả hoặc ra một quyết định về cách làm hồ sơ = một dòng**, ghi ngay trong lượt đó, cùng lúc với việc đưa quy tắc vào chuẩn.
- **Chuẩn trước, model sau** (user, 2026-11-11): quy tắc vào chuẩn của repo trước, rồi mới sửa model mẫu.
- Dòng nhật ký **không thay** quy tắc trong chuẩn: nó ghi lại *ai nói gì, khi nào, vì sao* và trỏ tới nơi quy tắc đang nằm. Claude làm việc theo chuẩn; nhật ký để soát lại và để biết việc nào còn treo.

| Cột | Nội dung |
|---|---|
| `id` | `<ngày>-<tên ngắn>` (chữ thường không dấu, số, gạch nối) |
| `date` | ngày user nói, `YYYY-MM-DD` |
| `scope` | `all`, hoặc khoá loại công trình (`villa`, `townhouse, villa`) |
| `kind` | khoá `Loại` của `drafting-coverage.md` §2; trống khi không riêng loại bản vẽ nào |
| `topic` | một, hai từ: `paper`, `text`, `naming`, `layout`, `sheet-list`, `stair-dims`… Dùng lại từ đã có (`corrections` liệt kê) |
| `rule` | điều user sửa, viết thành quy tắc, một câu |
| `why` | lý do user nêu; trống khi không nêu, không tự đoán |
| `status` | `open` = user đã nói, chuẩn chưa có · `in-repo` = đã nằm trong chuẩn · `superseded` = bị một lần sửa sau thay |
| `repo_ref` | nơi quy tắc đang nằm: `đường dẫn file` + § hoặc mã quy tắc; nhiều nơi cách nhau bằng `; ` |
| `superseded_by` | `id` của dòng thay nó |

- Không ghi tên khách hàng, tên model, số sheet, đường dẫn máy. Bài học chỉ đúng cho một dự án ở trong profile của dự án đó, không vào nhật ký.
- User đổi ý: thêm dòng mới, dòng cũ chuyển `superseded` + `superseded_by`. Không xoá, không sửa lại `rule` của dòng cũ.
- Dòng `open` là việc tồn của repo: trước khi làm một loại bản vẽ, tra dòng `open` của loại đó; dòng `open` thắng chuẩn hiện có khi hai bên khác nhau.
- Trên máy đã cài, công cụ không ghi nhật ký (lần cài sau sẽ ghi đè): đưa điều user sửa vào báo cáo cuối lượt để nó được ghi vào repo.

## DB3. Điều kiện của dòng bản vẽ

- Cột **Điều kiện** trong file bộ hồ sơ vẫn viết bằng lời. `data/condition-map.csv` đổi mỗi câu thành biểu thức khoá: `a&b` = cả hai, `a|b` = một trong hai (vd. `có hầm và hầm có trần` → `basement&basement-ceiling`).
- Khoá và nhãn: `data/condition-keys.csv`. Thêm một câu điều kiện mới vào file bộ hồ sơ → thêm một dòng vào `condition-map.csv` (và khoá mới nếu cần); `check` báo khi thiếu.
- `sheets --has <khoá, khoá>` giữ dòng không điều kiện và dòng có điều kiện đúng, rồi liệt kê các dòng bị loại kèm lý do (thành "n/a + lý do" trong sổ bộ hồ sơ). Không có `--has` thì mọi dòng có điều kiện đều hiện.

## DB4. Lệnh

```
python tools/standards-db/standards_db.py <lệnh>
```

| Lệnh | Làm gì |
|---|---|
| `types` | Các loại công trình: khổ giấy, có danh mục riêng hay cộng vào bộ lõi, số dòng |
| `conditions` | Các khoá điều kiện dùng cho `--has` |
| `sheets --type <loại> [--stage KT] [--has elevator,pool] [--kind stair] [--group 5] [--section <chữ>] [--max-level 1] [--detail]` | Bản vẽ bộ hồ sơ cần có, theo thứ tự sheet; dòng cuối: số dòng, khổ giấy, số dòng theo mức |
| `corrections [--scope villa] [--kind stair] [--topic paper] [--status open] [--text <chữ>] [--since <ngày>] [--detail]` | Nhật ký sửa; `--scope` lấy cả dòng `all` |
| `correction-add --date … --slug … --scope … --topic … --rule … [--kind …] [--why …] [--ref …]` | Thêm một dòng; có `--ref` thì `in-repo`, không thì `open` |
| `correction-set <id> [--ref …] [--status …] [--superseded-by <id>]` | Đổi trạng thái: `--ref` → `in-repo`, `--superseded-by` → `superseded` |
| `sql "SELECT …"` | Câu hỏi bất kỳ, chỉ đọc |
| `check` | Các file văn bản khớp nhau; CI chạy ở mỗi lần push |
| `build` | Ghi file `standards.db` |

Mọi lệnh đọc nhận `--json`. Kết quả mặc định ngắn; `--detail` thêm cột nội dung bắt buộc hoặc lý do.

## DB5. Cập nhật

- Sửa bảng trong file bộ hồ sơ hay ma trận độ phủ: không phải làm gì thêm cho database, chỉ chạy `check` (báo câu điều kiện chưa có khoá, `Nhóm` / `GĐ` sai, số ô của dòng lệch với đầu bảng).
- Thêm loại công trình: thêm file `drafting-sheet-set-<loại>.md`; database tự nhận.
- Dữ liệu mới chỉ vào database khi nó cần tra theo nhiều thuộc tính và có một file văn bản làm nguồn. Số liệu của dự án thật (số sheet, tên model) không vào repo.
