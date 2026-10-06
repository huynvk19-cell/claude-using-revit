# Claude using Revit — drafting architect

Bộ **skill** và **domain** để Claude Code (qua Revit MCP) triển khai bản vẽ Revit như một kiến trúc sư triển khai. Bộ này dùng chung cho mọi dự án.

Skills and domain standards that let Claude Code, via Revit MCP, do drawing production like a drafting architect: dims, tags, titles, viewports, crops and templates.

## Cấu trúc (Layout)

| Thư mục | Nội dung | Ngôn ngữ |
|---|---|---|
| `skills/` | Quy trình từng bước, ngắn, mỗi skill một chủ đề | English |
| `domain/` | Quy chuẩn bản vẽ: con số, vị trí, ngoại lệ, bẫy API | Tiếng Việt + thuật ngữ Revit |
| `templates/drafting-profile.md` | Giá trị riêng của từng dự án (dim type, model cấm chạm, view loại trừ…) | Tiếng Việt |
| `tools/crop.ps1` | Cắt vùng ảnh sheet để kiểm tra | PowerShell |

### Skills

| Skill | Dùng khi |
|---|---|
| `drafting-session` | Mở đầu mọi phiên vẽ, và khi "tiếp tục". Gồm luật cứng, phần mở đầu và phần kết thúc. |
| `drafting-review-round` | Xử lý một đợt comment (PDF + danh sách task) |
| `drafting-dims` | Dim trục, dim cao độ, đổi type dim |
| `drafting-opening-dims` | Dim cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (dim đứng + dim ngang) |
| `drafting-tags-titles` | Tag cửa, room tag, view title |
| `drafting-views-sheets` | Viewport, crop/scope box, đầu trục 2D, view template, link |
| `drafting-visual-check` | Xuất ảnh sheet và soát lỗi trình bày |

### Domain

| File | Nội dung |
|---|---|
| `drafting-work-rules.md` | Luật cứng, nhịp làm việc, cách báo cáo |
| `drafting-dimensions.md` | Chuẩn dim trục, dim cao độ, type dim |
| `drafting-opening-dims.md` | 5 quy tắc dim cửa sổ, cửa đi (Q1–Q5) |
| `drafting-annotation.md` | Chuẩn tag, room tag, title |
| `drafting-views-sheets.md` | Viewport, crop, dependent view, template |
| `drafting-tools.md` | Danh mục lệnh MCP theo chủ đề |
| `drafting-api-pitfalls.md` | Bẫy Revit API và cách tránh |

## Yêu cầu (Requirements)

- Claude Code.
- Revit có cài [Revit MCP](https://github.com/shuotao/REVIT_MCP_study), với dynamic commands. Tên lệnh trong `domain/drafting-tools.md` là các dynamic command. Lệnh nào chưa có thì phải viết thêm.

## Cài đặt (Install)

```powershell
git clone <repo-url>
cd claude-using-revit
powershell -ExecutionPolicy Bypass -File install.ps1
```

Lệnh trên copy:
- `skills/*` vào `~/.claude/skills/`
- `domain/`, `templates/`, `tools/` vào `~/.claude/drafting-domain/`

Mỗi dự án cần một file `drafting-profile.md`: copy từ `templates/` vào thư mục gốc dự án rồi điền.

## Cập nhật bài học (Keeping it alive)

Mỗi lần user sửa lại kết quả của Claude, ghi bài học thành **một dòng có ngày**:

| Loại bài học | Ghi vào đâu |
|---|---|
| Đúng cho mọi dự án | file `domain/` tương ứng → commit lên repo này |
| Chỉ đúng cho dự án đang làm | `drafting-profile.md` của dự án đó (không đưa lên repo) |

Không đưa tên khách hàng, tên model, số sheet hay đường dẫn máy vào repo này.
