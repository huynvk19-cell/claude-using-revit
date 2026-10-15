# Claude using Revit — drafting architect

Bộ **skill** và **domain** để Claude Code (qua Revit MCP) triển khai bản vẽ Revit như một kiến trúc sư triển khai. Bộ này dùng chung cho mọi dự án.

Skills and domain standards that let Claude Code, via Revit MCP, do drawing production like a drafting architect: dims, tags, titles, viewports, crops and templates.

## Cấu trúc (Layout)

| Thư mục | Nội dung | Ngôn ngữ |
|---|---|---|
| `workflows/` | **Quy trình**: một công việc trọn vẹn = các skill theo thứ tự, mỗi bước có điều kiện "xong khi" | English |
| `skills/` | Các bước làm của một chủ đề, ngắn, mỗi skill một chủ đề | English |
| `domain/` | Quy chuẩn bản vẽ: con số, vị trí, ngoại lệ, bẫy API | Tiếng Việt + thuật ngữ Revit |
| `agents/` | Subagent `drafting-visual-checker`: đọc ảnh kiểm tra, chỉ trả lời bằng chữ (ảnh không ở lại trong hội thoại chính) | English |
| `templates/drafting-profile.md` | Giá trị riêng của từng dự án (dim type, model cấm chạm, view loại trừ…) | Tiếng Việt |
| `revit-commands/` | 85 lệnh động (dynamic commands) C# cho Revit MCP: dim, tag, title, viewport, crop, lõi thang, dim bám tay vịn, filled region cột theo kết cấu, mặt cắt dọc thang (dim, tag, số bậc, cao độ tay vịn, ô hoàn thiện), kiểm tra mặt cắt ngang thang, kiểm tra chồng lắp… Không chứa giá trị riêng dự án | C# |
| `tools/crop.ps1` | Cắt vùng ảnh sheet để kiểm tra | PowerShell |
| `tools/work-status/` | Cửa sổ "Đang xử lý" luôn nổi trên màn hình (tuỳ chọn) | PowerShell |

### Skills

| Skill | Dùng khi |
|---|---|
| `drafting-session` | Mở đầu mọi phiên vẽ, và khi "tiếp tục". Gồm luật cứng, **làm theo đợt ≤10 view cùng loại**, **type đánh dấu** cho mọi phần tử tạo mới, **bảng chọn skill + domain theo việc** (chỉ đọc đúng file cần), phần mở đầu và phần kết thúc lượt. |
| `drafting-workflow` | **Chạy quy trình**: chọn một file trong `workflows/`, gom mọi view một lần, ghi checklist (bước × view) ra file, làm từng bước cho tất cả view rồi mới sang bước sau, mỗi ô phải đạt "xong khi", không bỏ bước im lặng, "tiếp tục" chạy tiếp từ ô trống đầu tiên |
| `drafting-start` | **Bắt đầu việc**: mở các view đang làm (zoom theo crop), ẩn Properties + Project Browser, bật cửa sổ trạng thái |
| `drafting-end` | **Kết thúc làm việc**: ghi log, bật lại Properties + Project Browser như lúc đầu, đóng cửa sổ trạng thái, báo cáo |
| `drafting-review-round` | Xử lý một đợt comment (PDF + danh sách task) |
| `drafting-grid-dims` | Dim trục theo G1–G4: mỗi nhóm trục song song đúng 1 dim cách trục + 1 dim tổng, nằm giữa mép crop và bubble, annotation crop kéo ra tới bubble |
| `drafting-dims` | Dim cao độ, đổi type dim (dim trục → `drafting-grid-dims`) |
| `drafting-opening-dims` | Dim cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (dim đứng + dim ngang) |
| `drafting-opening-tags` | Tag cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (T1–T8) |
| `drafting-stair-section-parallel` | **Mặt cắt dọc** thang bộ (cắt song song stair path, LA–LC): chiều cao vế `169.4mm x 16R = 2710 (EQUAL RISERS)`, cao độ tầng, chiều dài vế, thông thuỷ dưới chiếu nghỉ, tay vịn; tag vế, P01, cao độ, F../C.., cửa; số bậc liên tục |
| `drafting-stair-section-cross` | **Mặt cắt ngang** thang bộ (cắt không song song stair path, vế nhìn thẳng đầu, XA–XC): chuỗi chiều cao từng dải vế có công thức, cao độ tầng, chuỗi thông thuỷ | dày chiếu nghỉ liên tục, bề rộng tới tay vịn, tường; tag vế chữ đứng hai bên, tay vịn, cao độ, F../C..; số bậc Start and End trên vế thấy cổ bậc |
| `drafting-stair-plan` | **Mặt bằng** lõi thang bộ (SA1–SD; mặt cắt thang sẽ là skill riêng): 3 vế + mọi chiếu nghỉ thấy được (kể cả dưới View Depth), dim tay vịn không cần dim tay, dim thông thuỷ (CLEAR chỉ trên bề rộng vế; mép lan can → mép bậc → tường), chiều dài vế có công thức (bậc trên cùng ngang chiếu nghỉ không tính), chuỗi chiếu nghỉ / tổng khép tới tường, tường/cửa tới trục; tag vế, tay vịn, cao độ, cửa, hoàn thiện; đếm/đánh số bậc từng vế; stair path chỉ có mũi tên |
| `drafting-tags-titles` | Room tag, view title |
| `drafting-views-sheets` | Viewport, crop/scope box, đầu trục 2D, view template, link |
| `drafting-visual-check` | Xuất ảnh sheet và soát lỗi trình bày |

### Quy trình (Workflows)

Quy trình là lớp trên skill: nó không chứa luật vẽ, chỉ nói **làm bước nào (một skill, hoặc một mục của chuẩn), theo thứ tự nào, khi nào coi là xong, khi nào được bỏ**. Đầu quy trình gom **mọi view / sheet** của công việc một lần; mỗi bước làm cho **tất cả view** rồi mới sang bước sau (vd. lõi thang: tag vế cho mọi mặt bằng → dim thông thuỷ cho mọi mặt bằng → …). Skill `drafting-workflow` chạy quy trình và giữ checklist `workflow-<ngày>-<tên>.md` (trong thư mục đợt comment, hoặc `log/`): mỗi ô bước × view là `x` xong, `-` bỏ (có lý do), `!` chưa xong (có lý do) hoặc `n/a`. Báo cáo cuối không được còn ô trống.

| Quy trình | Các bước (skill) |
|---|---|
| `elevation-section` | start → views-sheets → grid-dims → dims (cao độ) → opening-dims → opening-tags → tags-titles (title) → visual-check → end |
| `floor-plan` | start → views-sheets → grid-dims → tags-titles (room tag) → tags-titles (title) → visual-check → end |
| `stair-core` | start → **A mặt bằng** (khảo sát → tag vế SB1 → dim thông thuỷ SA1 → chiếu nghỉ SA3 → chiều dài vế SA2 → tường/cửa → trục SA4 → số bậc → stair path → tag lan can SB2 → cao độ SB3 → F.. SB5 → W.. SB6 → tag cửa SB4 → X thông tầng → dọn chồng → kiểm lại) → **B mặt cắt dọc** (LB1 → LA1 → LA2 → LA3 → LA4 → LA5 → LA6 → LC → LB2 → LB3 → LB4 → LB5 → LB6 → dọn → kiểm lại) → **C mặt cắt ngang** (XB1 → XA1 → XA2 → XA3 → XA4 → XA5 → XA6 → XC → XB2 → XB3 → XB4 → XB5 → XB6 → dọn → kiểm lại) → visual-check → end |
| `review-round` | review-round (đọc + phân loại) → start → skill theo từng task → tags-titles (title) → visual-check → end |

Thêm quy trình mới: một file `workflows/<tên>.md` + một dòng trong bảng của `drafting-workflow` + một dòng ở bảng trên.

### Domain

| File | Nội dung |
|---|---|
| `drafting-work-rules.md` | Luật cứng, nhịp làm việc, làm theo đợt + type đánh dấu (§2c), cách báo cáo |
| `drafting-grid-dims.md` | 4 quy tắc dim trục (G1–G4): nhóm trục, 1 chain + 1 overall, dải giữa crop và bubble, annotation crop |
| `drafting-stair-section-parallel.md` | Mặt cắt dọc thang bộ: vế F1…, công thức R/T, lớp dim, tag, số bậc (LA–LC) |
| `drafting-stair-section-cross.md` | Mặt cắt ngang thang bộ: dải vế, hướng vế (away / toward), lớp dim, chuỗi thông thuỷ liên tục, tag, số bậc Start and End (XA–XC) |
| `drafting-stair-plan.md` | Mặt bằng lõi thang bộ: 3 vế V1/V2/V3, lớp dim, mép thông thuỷ, CLEAR chỉ cho bề rộng vế, công thức chiều dài vế, tag, đếm bậc, stair path (SA1–SD) |
| `drafting-dimensions.md` | Chuẩn dim cao độ, type dim (dim trục → `drafting-grid-dims.md`) |
| `drafting-opening-dims.md` | 5 quy tắc dim cửa sổ, cửa đi (Q1–Q5) |
| `drafting-opening-tags.md` | 8 quy tắc tag cửa sổ, cửa đi (T1–T8) |
| `drafting-annotation.md` | Room tag, view title, leader vuông góc |
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
- `workflows/` vào `~/.claude/drafting-domain/workflows/`
- `agents/*.md` vào `~/.claude/agents/`
- `domain/`, `templates/`, `tools/` vào `~/.claude/drafting-domain/`
- `revit-commands/*.cs` vào thư mục dynamic-commands của Revit MCP (nếu có `-CommandsDir` hoặc biến môi trường `REVIT_MCP_COMMANDS`)
- `-WorkStatus`: script cửa sổ trạng thái vào `%USERPROFILE%\Tools\WorkStatus`

**Lệnh dùng chung, giá trị theo dự án**: tool không ghi cứng tên type hay tên family của dự án nào. Tên dim type (`dimTypeName` / `typeName`), family cửa cuốn (`nominalFamilies` / `rollupFamilies`)… được truyền vào khi gọi, lấy từ `drafting-profile.md` của dự án. Thiếu tham số bắt buộc thì tool báo lỗi, không tự đoán.

Mỗi dự án cần một file `drafting-profile.md`: copy từ `templates/` vào thư mục gốc dự án rồi điền.

## Làm theo đợt và type đánh dấu (Batches, mark types)

- **Đợt**: Claude không làm xong từng view rồi báo, mà gom tối đa **10 view cùng loại** (cùng ViewType: mặt bằng với mặt bằng, mặt cắt với mặt cắt…, cùng chủ đề) và chạy từng bước cho cả đợt, cuối đợt báo một lần. Mặt đứng / mặt cắt: lệnh vẫn gọi mỗi lần một view (chống treo Revit), nhưng chạy liền cả đợt.
- **Type đánh dấu**: mọi phần tử Claude tạo mới (dim, spot, tag, room tag, stair path, filled region, text…) mang type `<type chính thức><hậu tố>`, hậu tố đặt trong `drafting-profile.md` (vd. ` - check`). Lệnh `mark_types`: `snapshot` đầu đợt → `mark` cuối đợt → `list` → `restore`.
- **Trả về chính thức** (khi đã duyệt): chọn một phần tử → Select All Instances → In Entire Project → đổi sang type chính thức; hoặc `mark_types {action:"restore"}` làm cho mọi type đánh dấu một lần.
- Không đánh dấu được: detail line (chỉ có line style) và chỉnh sửa trên phần tử có sẵn (dời tag, đầu trục 2D, crop): xem log `apply`.

## Tiết kiệm token (Token use)

Mỗi bước Claude gọi tool, toàn bộ phần đầu phiên (danh sách tool, mô tả skill, CLAUDE.md) và hội thoại được gửi lại cho model. Phần cố định càng dài, mỗi bước càng tốn; hội thoại càng dài (kết quả lệnh, ảnh), mỗi bước sau càng tốn.

| Phần | Tốn (mỗi bước) | Cách giảm | Trạng thái |
|---|---|---|---|
| Tool có sẵn của Revit MCP (profile `full`, 192 tool) | ~36.6k token nếu nạp sẵn; chỉ tên (~1.5k) khi tool search bật | **A**: profile `drafting` (26 tool) | cần làm trên máy |
| 85 lệnh của repo (header `mcp-tool`) | ~11.8k token nếu nạp sẵn; chỉ tên (~0.8k) khi tool search bật; mỗi lệnh đã nạp ~140 token | **B**: header ngắn, giải thích ở `// ---- Details` | đã làm |
| Tool search của Claude Code | — | **C**: giữ bật (mặc định), kiểm tra bằng `/context` | kiểm tra trên máy |
| Nạp lệnh từng cái một (mỗi lần một lượt) | 1 lượt / lệnh | **D**: dòng **Tools** trong mỗi skill → nạp hết trong 1 lần ToolSearch | đã làm |
| Mô tả skill (gửi mỗi request) | 6.8k → 5.3k ký tự | mô tả ngắn, luật nằm trong thân skill | đã làm |
| Ảnh kiểm tra (mỗi ảnh ~1.5–2k token, ở lại trong hội thoại) | theo số ảnh | **E**: subagent `drafting-visual-checker` (Sonnet) đọc ảnh, chỉ trả chữ | đã làm |
| Kết quả lệnh | theo lệnh | **F**: mặc định ngắn (số lượng + lỗi), chi tiết sau cờ `verbose`/`detail` | luật cho lệnh mới (`CLAUDE.md`) |

### A — Profile `drafting` cho Revit MCP

Revit MCP chọn nhóm tool có sẵn theo biến môi trường `MCP_PROFILE` (`full` mặc định, `architect`, `mep`, `structural`, `fire-safety`). Bộ skill drafting chỉ cần nhóm cơ bản (`get_active_view`…) và các lệnh động. Khi tool search bật, profile vẫn có lợi: danh sách tên ngắn hơn, ToolSearch ít trả nhầm tool không liên quan.

1. Áp patch `tools/revit-mcp/drafting-profile.patch` vào thư mục Revit MCP (thêm profile `drafting: [baseTools]` trong `MCP-Server/src/tools/index.ts`), rồi build lại: `cd MCP-Server && npm run build`.
2. Thêm `MCP_PROFILE` vào cấu hình MCP của Claude:
   ```json
   "revit-mcp": { "command": "node", "args": ["<…>/MCP-Server/build/index.js"], "env": { "MCP_PROFILE": "drafting" } }
   ```
3. Mở phiên Claude Code mới, kiểm tra: `hello_revit` và các lệnh động vẫn gọi được.
   - Bản Revit MCP có lệnh động (`dynamic-commands`, `run_dynamic_command`) là bản riêng, không có trong mã gốc. Nếu lệnh động nằm trong một module của `PROFILE_MODULES`, thêm module đó vào `drafting`.
   - Đặt `drafting` mà **chưa** áp patch → Revit MCP tự quay về `full` (không hỏng, chỉ không tiết kiệm).
4. Việc cần tool khác (MEP, room, tạo view…) → đổi lại `MCP_PROFILE` = `architect` hoặc `full`.

### C — Tool search (Claude Code)

Claude Code bản mới **mặc định** hoãn nạp tool MCP: đầu phiên chỉ gửi tên tool, schema chỉ nạp khi Claude tìm (ToolSearch). Nó tự tắt (nạp sẵn mọi tool, ~48k token mỗi bước) khi:
- `ANTHROPIC_BASE_URL` trỏ tới proxy không phải của Anthropic → đặt `"ENABLE_TOOL_SEARCH": "true"` trong `env` của `~/.claude/settings.json` (chỉ khi proxy hỗ trợ `tool_reference`);
- có `ENABLE_TOOL_SEARCH=false` hoặc `CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS`;
- model cũ hơn thế hệ 4.5.

Kiểm tra: gõ `/context` trong phiên có Revit MCP. Tool MCP phải hiện là deferred (hoãn), không chiếm hàng chục nghìn token. Nếu cấu hình `revit-mcp` có `"alwaysLoad": true` thì bỏ đi. Cập nhật Claude Code (`claude update`) nếu bản cũ.

Dự phòng khi không bật được tool search: cho Revit MCP chỉ đăng ký một tool `run_dynamic_command {name, args}` thay vì mỗi lệnh một tool (sửa trong bản Revit MCP riêng của bạn); Claude tra tham số trong `drafting-tools.md` và header `.cs`.

### Cách làm việc ít token

- **Model**: việc đã có skill (chạy lại quy trình quen) → `/model sonnet`; chủ đề mới, viết lệnh C# mới → Opus.
- **Một chủ đề một phiên**: đổi chủ đề → `/clear`. Trạng thái nằm ở `TASKS.md`, `log/`, `drafting-profile.md`, không cần kéo hội thoại dài rồi `/compact`.
- **Lệnh một phát**: mỗi chủ đề nên có một lệnh làm cả khảo sát → áp dụng → kiểm tra (như `stair_plan_annotate`, `grid_dims_band`), mỗi view chỉ còn vài lần gọi: preview, apply, kiểm tra.
- **Chạy hàng loạt không cần ngồi canh** (máy có Revit đang mở, từng phiên một vì Revit API chỉ chạy một luồng):
  ```powershell
  claude -p "Dùng skill drafting-grid-dims cho các view <danh sách>. Ghi log, báo cáo Việc tồn." --model sonnet --allowedTools "mcp__revit-mcp" "Read" "Edit" "Write" "Grep" "Glob" "Agent"
  ```
  `mcp__revit-mcp` cho phép mọi tool của server `revit-mcp` (đổi theo tên server trong cấu hình của bạn). Sáng hôm sau xem log và ảnh.

## Cập nhật bài học (Keeping it alive)

Mỗi lần user sửa lại kết quả của Claude, ghi bài học thành **một dòng có ngày**:

| Loại bài học | Ghi vào đâu |
|---|---|
| Đúng cho mọi dự án | file `domain/` tương ứng → commit lên repo này |
| Chỉ đúng cho dự án đang làm | `drafting-profile.md` của dự án đó (không đưa lên repo) |

Bài học đã được một lệnh tự bảo đảm, hoặc đã gộp vào một quy tắc của chuẩn → xoá dòng bài học đó (chuẩn càng ngắn, mỗi việc càng ít token).

Không đưa tên khách hàng, tên model, số sheet hay đường dẫn máy vào repo này.
