# Claude using Revit — drafting architect

Bộ **skill** và **domain** để Claude Code (qua Revit MCP) triển khai bản vẽ Revit như một kiến trúc sư triển khai. Bộ này dùng chung cho mọi dự án.

Skills and domain standards that let Claude Code, via Revit MCP, do drawing production like a drafting architect: dims, tags, titles, viewports, crops and templates.

## Cấu trúc (Layout)

| Thư mục | Nội dung | Ngôn ngữ |
|---|---|---|
| `skills/` | Quy trình từng bước, ngắn, mỗi skill một chủ đề | English |
| `domain/` | Quy chuẩn bản vẽ: con số, vị trí, ngoại lệ, bẫy API | Tiếng Việt + thuật ngữ Revit |
| `templates/drafting-profile.md` | Giá trị riêng của từng dự án (dim type, model cấm chạm, view loại trừ…) | Tiếng Việt |
| `revit-commands/` | 51 lệnh động (dynamic commands) C# cho Revit MCP: dim, tag, title, viewport, crop, lõi thang, kiểm tra chồng lắp… Không chứa giá trị riêng dự án | C# |
| `tools/crop.ps1` | Cắt vùng ảnh sheet để kiểm tra | PowerShell |
| `tools/work-status/` | Cửa sổ "Đang xử lý" luôn nổi trên màn hình (tuỳ chọn) | PowerShell |

### Skills

| Skill | Dùng khi |
|---|---|
| `drafting-session` | Mở đầu mọi phiên vẽ, và khi "tiếp tục". Gồm luật cứng, **bảng chọn skill + domain theo việc** (chỉ đọc đúng file cần), phần mở đầu và phần kết thúc lượt. |
| `drafting-start` | **Bắt đầu việc**: mở các view đang làm (zoom theo crop), ẩn Properties + Project Browser, bật cửa sổ trạng thái |
| `drafting-end` | **Kết thúc làm việc**: ghi log, bật lại Properties + Project Browser như lúc đầu, đóng cửa sổ trạng thái, báo cáo |
| `drafting-review-round` | Xử lý một đợt comment (PDF + danh sách task) |
| `drafting-grid-dims` | Dim trục theo G1–G4: mỗi nhóm trục song song đúng 1 dim cách trục + 1 dim tổng, nằm giữa mép crop và bubble, annotation crop kéo ra tới bubble |
| `drafting-dims` | Dim cao độ, đổi type dim (dim trục → `drafting-grid-dims`) |
| `drafting-opening-dims` | Dim cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (dim đứng + dim ngang) |
| `drafting-opening-tags` | Tag cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (T1–T8) |
| `drafting-stair-plan` | **Mặt bằng** lõi thang bộ (SA1–SD; mặt cắt thang sẽ là skill riêng): dim thông thuỷ CLEAR, chiều dài vế có công thức, chiếu nghỉ, tường/cửa tới trục; tag vế, tay vịn, cao độ, cửa, hoàn thiện; đếm/đánh số bậc từng vế; stair path chỉ có mũi tên |
| `drafting-tags-titles` | Room tag, view title |
| `drafting-views-sheets` | Viewport, crop/scope box, đầu trục 2D, view template, link |
| `drafting-visual-check` | Xuất ảnh sheet và soát lỗi trình bày |

### Domain

| File | Nội dung |
|---|---|
| `drafting-work-rules.md` | Luật cứng, nhịp làm việc, cách báo cáo |
| `drafting-grid-dims.md` | 4 quy tắc dim trục (G1–G4): nhóm trục, 1 chain + 1 overall, dải giữa crop và bubble, annotation crop |
| `drafting-stair-plan.md` | Mặt bằng lõi thang bộ: 3 vế V1/V2/V3, lớp dim, CLEAR, công thức chiều dài vế, tag, đếm bậc, stair path (SA1–SD) |
| `drafting-dimensions.md` | Chuẩn dim cao độ, type dim (dim trục → `drafting-grid-dims.md`) |
| `drafting-opening-dims.md` | 5 quy tắc dim cửa sổ, cửa đi (Q1–Q5) |
| `drafting-opening-tags.md` | 8 quy tắc tag cửa sổ, cửa đi (T1–T8) |
| `drafting-annotation.md` | Room tag, view title |
| `drafting-views-sheets.md` | Viewport, crop, dependent view, template |
| `drafting-tools.md` | Danh mục lệnh MCP theo chủ đề |
| `drafting-api-pitfalls.md` | Bẫy Revit API và cách tránh |

**Chỉ đọc file cần dùng**: Claude không đọc hết repo. `drafting-session` có bảng việc → skill → domain; mỗi skill chỉ mở đúng một file chuẩn của nó. `drafting-tools.md` và `drafting-api-pitfalls.md` chỉ tra (search) dòng cần. Khi làm việc ngay trong repo, `CLAUDE.md` nhắc lại quy tắc này.

## Yêu cầu (Requirements)

- Claude Code.
- Revit có cài [Revit MCP](https://github.com/shuotao/REVIT_MCP_study) bản có dynamic commands (thư mục `dynamic-commands`: file `.cs` được biên dịch ngay trong Revit, không cần khởi động lại).
- Các lệnh trong `revit-commands/` viết và chạy thử trên Revit 2023 (chưa thử trên bản khác).

## Cài đặt (Install)

```powershell
git clone <repo-url>
cd claude-using-revit
powershell -ExecutionPolicy Bypass -File install.ps1 -CommandsDir "<đường dẫn>\REVIT_MCP_study\dynamic-commands" -WorkStatus
```

Lệnh trên copy:
- `skills/*` vào `~/.claude/skills/`
- `domain/`, `templates/`, `tools/` vào `~/.claude/drafting-domain/`
- `revit-commands/*.cs` vào thư mục dynamic-commands của Revit MCP (nếu có `-CommandsDir` hoặc biến môi trường `REVIT_MCP_COMMANDS`)
- `-WorkStatus`: script cửa sổ trạng thái vào `%USERPROFILE%\Tools\WorkStatus`

**Lệnh dùng chung, giá trị theo dự án**: tool không ghi cứng tên type hay tên family của dự án nào. Tên dim type (`dimTypeName` / `typeName`), family cửa cuốn (`nominalFamilies` / `rollupFamilies`)… được truyền vào khi gọi, lấy từ `drafting-profile.md` của dự án. Thiếu tham số bắt buộc thì tool báo lỗi, không tự đoán.

Mỗi dự án cần một file `drafting-profile.md`: copy từ `templates/` vào thư mục gốc dự án rồi điền.

## Cập nhật bài học (Keeping it alive)

Mỗi lần user sửa lại kết quả của Claude, ghi bài học thành **một dòng có ngày**:

| Loại bài học | Ghi vào đâu |
|---|---|
| Đúng cho mọi dự án | file `domain/` tương ứng → commit lên repo này |
| Chỉ đúng cho dự án đang làm | `drafting-profile.md` của dự án đó (không đưa lên repo) |

Không đưa tên khách hàng, tên model, số sheet hay đường dẫn máy vào repo này.
