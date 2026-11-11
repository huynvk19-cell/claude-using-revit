# Claude using Revit — drafting architect

Bộ **skill** và **domain** để Claude Code triển khai bản vẽ Revit như một kiến trúc sư triển khai, cùng add-in Revit **claude using revit**: vừa là tab công cụ của người dùng, vừa là máy chủ MCP để Claude làm việc trong Revit (không cần pyRevit hay Revit MCP nào khác). Bộ này dùng chung cho mọi dự án.

Skills and domain standards that let Claude Code do drawing production like a drafting architect (dims, tags, titles, viewports, crops and templates), and the Revit add-in claude using revit: the user's ribbon tools and the MCP server Claude works through, in one add-in.

## Cấu trúc (Layout)

| Thư mục | Nội dung | Ngôn ngữ |
|---|---|---|
| `workflows/` | **Quy trình**: một công việc trọn vẹn = các skill theo thứ tự, mỗi bước có điều kiện "xong khi" | English |
| `skills/` | Các bước làm của một chủ đề, ngắn, mỗi skill một chủ đề | English |
| `domain/` | Quy chuẩn bản vẽ: con số, vị trí, ngoại lệ, bẫy API | Tiếng Việt + thuật ngữ Revit |
| `agents/` | Subagent `drafting-visual-checker`: đọc ảnh kiểm tra, chỉ trả lời bằng chữ (ảnh không ở lại trong hội thoại chính) | English |
| `templates/drafting-profile.md` | Giá trị riêng của từng dự án (dim type, model cấm chạm, view loại trừ…) | Tiếng Việt |
| `addin/` | **Add-in claude using revit** (C#, Revit 2023–2027): tab ribbon **claude using revit** dựng từ `addin/extension/`, chạy script Python bằng IronPython có sẵn trong add-in; **máy chủ MCP** `http://127.0.0.1:8970/mcp` (Streamable HTTP, Claude nối thẳng, không cần Node); mỗi file `revit-commands/*.cs` là một tool, biên dịch (Roslyn) lần gọi đầu và mỗi khi file đổi; tool có sẵn `cur_task`, `cur_status`, `run_dynamic_command`; nhiều cửa sổ Revit: cửa sổ đầu giữ cổng, các cửa sổ khác chờ và nhận khi cổng rảnh hoặc khi bấm *Nhận kết nối*; panel **Claude**: nút trạng thái (xanh lá = đã kết nối / đang chạy, xanh dương = chờ Claude, vàng = cửa sổ khác giữ cổng, xám = tắt) mở cửa sổ chi tiết (client, lệnh gần đây, bật / tắt, chép lệnh nối Claude Code), nút **Tải lại** (chạy lại phần Python). `addin/build.ps1` build, `addin/tests/` kiểm tra không cần Revit (biên dịch mọi lệnh theo API từng bản Revit; máy chủ MCP + client MCP chính thức) | C# + IronPython |
| `revit-commands/` | 99 lệnh MCP C# (mỗi file một tool của claude using revit): dim, tag, title, viewport, crop, lõi thang, dim bám tay vịn, filled region cột theo kết cấu, mặt cắt dọc thang (dim, tag, số bậc, cao độ tay vịn, ô hoàn thiện), kiểm tra mặt cắt ngang thang, kiểm tra chồng lắp… Không chứa giá trị riêng dự án | C# |
| `tools/crop.ps1` | Cắt vùng ảnh sheet để kiểm tra | PowerShell |
| `tools/revit-port/` | Cửa sổ Revit nào đang phục vụ Claude (cổng MCP của claude using revit), document đang mở ở từng cửa sổ, chuyển cổng sang cửa sổ khác (`status` / `take -RevitPid N` / `release`) | PowerShell |
| `tools/dwg-bind-xref/` | Gộp xref của DWG thành một file `_merged.dwg` (AutoCAD accoreconsole), có menu chuột phải Explorer | PowerShell |
| `addin/extension/` | Các nút của tab **claude using revit** (mỗi nút một thư mục `*.pushbutton`: `script.py`, `bundle.yaml`, `icon.png`; API của host: `lib/cur/`): panel **Đánh dấu**: nút **Tô đối tượng** (Filled Region đỏ, trong suốt 50%, bao quanh từng đối tượng đang chọn, trong view chứa nó) và **Xoá vùng tô** (chỉ xoá highlight do chính mình tạo; Shift+click: chỉ view đang mở); panel **Kiểm tra tag**: **Tìm chưa tag** (một cửa sổ: chọn phạm vi + loại đối tượng, xem trước số room, cửa đi, cửa sổ, tường chưa có tag và sàn chưa có cao độ, tô cam, rồi bấm một view để mở nó và chọn sẵn các đối tượng đó) và **Xoá vùng tô** (chỉ xoá các vùng tô cam do chính mình tạo); panel **In ấn**: **Print set theo DM** (chọn một schedule danh mục bản vẽ, xem sheet được thêm / bỏ, rồi tạo hoặc cập nhật print set mang đúng tên schedule, chứa đúng các sheet schedule đang hiện, bỏ placeholder; print set do người khác sửa lần cuối phải xác nhận mới ghi đè; Shift+click: cập nhật ngay schedule dùng lần trước; tự đánh lại No. nếu schedule có cột này) và **Chuẩn cột danh mục** (tick một hay nhiều schedule danh mục: No., Sheet Number, Sheet Name, SCALE (tham số sheet tên SCALE; model chưa có thì báo, không tự tạo), Revision, Revision Date lên đầu bảng, Checked By / Approved By / Designed By / Drawn By thêm ở dạng ẩn; chỉ thêm, không xoá cột hay tham số; No. là tham số sheet tool tự tạo và tự đánh 1..n theo thứ tự schedule, placeholder có số, sheet người khác đang giữ bị bỏ qua; nút **Hiện / Ẩn cột ký** bật tắt cùng lúc 4 cột ký trong các schedule đang chọn, không đụng cột ẩn khác; ô **Gán một giá trị cho mọi sheet**: chọn một tham số chữ của sheet (không có Sheet Number, Sheet Name, No.), gõ giá trị, Gán cho mọi sheet của các schedule đang chọn, chỉ ghi sheet có giá trị khác, bỏ qua sheet người khác đang giữ; Shift+click: áp ngay cho các schedule dùng lần trước) và **Xuất PDF** (đứng ở schedule danh mục thì dùng danh mục đó, đứng ở chỗ khác thì chọn danh mục; cửa sổ liệt kê các sheet của danh mục theo thứ tự schedule, mặc định chọn hết (hoặc các sheet đang chọn trong Revit), lọc theo số / tên, bỏ tick sheet không cần, chọn thư mục, tuỳ chọn chia thư mục con theo một tham số của sheet (gợi ý theo cách Project Browser nhóm sheet), bấm Xuất N file. Khổ giấy theo khung tên, zoom 100 %, căn giữa, vector; mỗi sheet một file PDF riêng trong thư mục đã chọn, không gộp thành một file. Sheet nặng xuất từng phần rồi chồng lại thành một trang, khung tên không in hai lần; giữa các sheet giải phóng bộ nhớ, kiểm tra RAM, thiếu thì dừng an toàn và bấm lại là xuất tiếp; Revit có văng (thường do một view lỗi hình học, không phải RAM) thì mở lại và bấm lần nữa: tool nhớ chỗ văng, tách từng view để tìm view lỗi, xuất riêng view đó, văng tiếp thì chia view thành ô theo vùng crop rồi ghép lại; không bao giờ bỏ view (luôn xuất vector, không raster); Shift+click xoá trí nhớ văng sau khi sửa model. Shift+click: thử lại view / sheet đã bỏ); panel **3D**: **3D theo view** (đứng ở mặt bằng, mặt bằng trần, mặt cắt hay mặt đứng, hoặc chọn một viewport trên sheet, bấm: mở view 3D riêng của bạn `3D Box - <tên Revit>`, dùng lại mỗi lần, với section box ôm đúng phần view thể hiện: mặt bằng theo crop + view range, mặt cắt / mặt đứng theo crop + far clip; phase theo view gốc; Shift+click: mặt bằng cắt đúng ở cut plane) | IronPython |
| `addin/extension/lib/liquid_glass.*`, `native_blur.py` | **Giao diện Liquid Glass** dùng chung cho mọi cửa sổ của tab: nền là Acrylic của Windows 11 (blur thật của DWM, chạy theo khi kéo cửa sổ, không chụp màn hình; Windows cũ hơn thì nền trắng mờ), kéo cửa sổ từ mọi chỗ trống, cùng các hộp thoại `alert`, `choose`, `Progress` | IronPython + WPF |
| `addin/extension/lib/tasks.py` | **Task dùng chung**: mỗi việc lặp lại viết một lần bằng Python; nút ribbon và Claude (tool MCP `cur_task {task, args}`) chạy đúng cùng code đó, nên Claude không phải tự làm lại từng bước. Hiện có: print set theo danh mục, chuẩn cột danh mục, cột ký, gán giá trị cho mọi sheet, xuất PDF theo danh mục, tìm / tô / xoá chưa tag, highlight, 3D theo view, lưới 3D cho viewer GPU. `list_tasks` liệt kê; thêm task mới không cần sửa C# | IronPython + C# |
| `addin/extension/lib/view_filter.py` + nút *Thêm vào filter* | Phần tử chọn trực tiếp trong view, kể cả trong file link, vào filter của view template: chọn tham số để lọc, filter mới hoặc có sẵn, Hide hoặc No hide (task `filter_selection`) | IronPython + WPF |
| `addin/extension/lib/pane.py` + `pane.xaml`, `table_ops.py` | **Cửa sổ Bảng & công cụ** (nút *Bảng & công cụ*, WPF của add-in, thuộc Revit và nổi trên Revit như các tool khác, blur của Windows, đổi cỡ được, vẫn làm việc trong Revit được): tab *Bảng* = mọi schedule sửa như Excel, danh sách luôn theo model: schedule mới hiện khi mở xổ xuống, mở schedule nào trong Revit thì Bảng theo schedule đó (phím, Ctrl+C / V với Excel, Ctrl+D / R, đánh số, tìm & thay, công thức, sort, lọc từng cột, hoàn tác, thêm sheet, xuất / nhập .xlsx .csv, ghi có xem trước, print set cùng tên tự đồng bộ); tab *Công cụ* = còn việc gì | IronPython + WPF |
| `addin/extension/lib/workflow_window.py` + `workflow.xaml`, `workflow_lib.py`, `content_update.py` | **Nút Workflow**: các workflow, skill và step card đang cài trên máy, mỗi mức một cửa sổ nhỏ của add-in (đọc thẳng từ file, card mặc định gập, ghim để giữ cửa sổ). Skill có phần `view_develop` hiện kết quả từng view và tô vàng card còn phải làm. *Giao cho Claude*: chọn phạm vi, chép tin nhắn để dán vào project chat. Tự cập nhật skill, workflow, card, tiêu chuẩn và lệnh MCP từ GitHub khi mở Revit, không dùng Claude (`"autoUpdate": false` trong `config.json` để tắt); code của add-in vẫn phải đóng Revit để cài lại. |
| `webapp/` + `addin/extension/lib/web_bridge.py` | **Web app** (tuỳ chọn, trình duyệt / iPad; add-in không cần): mọi nút của tab claude using revit (Highlight, Chưa tag, Print set theo DM, Chuẩn cột danh mục, Xuất PDF / DWG, 3D theo view, viewer 3D vẽ bằng GPU), **Bảng** (mọi schedule sửa như Excel: copy / paste với Excel, fill, đánh số, tìm & thay, xuất / nhập .xlsx) và mọi lệnh Revit MCP (task, lệnh của thư mục lệnh, tool có sẵn của add-in, form tự sinh từ schema) trong trình duyệt, giao diện kiểu Apple Vision (kính mờ trên nền thiên hà có vành đai; chỉ công cụ còn việc mới lên màu), dùng được từ iPad khi bật LAN. Máy chủ Node không cần thư viện (`webapp/server.mjs`, cổng 8780, mã truy cập trong `%APPDATA%\claude-using-revit\webapp.json`); phía Revit chỉ là cầu nối mỏng do claude using revit khởi động trong mỗi cửa sổ (127.0.0.1, cổng 48990 trở lên, có khoá), chạy đúng code của nút và của tool MCP trong add-in, **không dùng cổng MCP (8970)** nên không tranh với Claude. Xem `webapp/README.md` | Node + IronPython |

### Skills

| Skill | Dùng khi |
|---|---|
| `drafting-session` | Mở đầu mọi phiên vẽ, và khi "tiếp tục". Gồm luật cứng, **làm theo đợt ≤10 view cùng loại**, **type đánh dấu** cho mọi phần tử tạo mới, **bảng chọn skill + domain theo việc** (chỉ đọc đúng file cần), phần mở đầu và phần kết thúc lượt. |
| `drafting-workflow` | **Chạy quy trình**: chọn một file trong `workflows/`, bước 0 gom mọi view / sheet của công việc một lần (sheet → mọi view trên sheet, chia đợt ≤ 10 view, snapshot mọi đợt), ghi checklist (bước × view) ra file, làm từng bước cho tất cả view rồi mới sang bước sau, mỗi ô phải đạt "xong khi", không bỏ bước im lặng, "tiếp tục" chạy tiếp từ ô trống đầu tiên |
| `drafting-start` | **Bắt đầu việc**: mở các view đang làm (zoom theo crop) |
| `drafting-end` | **Kết thúc làm việc**: ghi log, báo cáo |
| `drafting-review-round` | Xử lý một đợt comment (PDF + danh sách task) |
| `drafting-grid-dims` | Dim trục theo G1–G5: mỗi nhóm trục song song đúng 1 dim cách trục + 1 dim tổng, nằm giữa mép crop và bubble, annotation crop kéo ra tới bubble, bubble không bị che (mỗi phía một hàng, bubble chồng thì elbow) |
| `drafting-dims` | Dim cao độ, đổi type dim (dim trục → `drafting-grid-dims`) |
| `drafting-opening-dims` | Dim cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (dim đứng + dim ngang) |
| `drafting-opening-tags` | Tag cửa sổ, cửa đi, cửa cuốn trên mặt đứng/mặt cắt (T1–T8) |
| `drafting-section-general` | **Khai triển mặt cắt chung** (thường 1/200): thẻ Fill kết cấu bị cắt (SF1–SF10) |
| `drafting-stair-section-parallel` | **Mặt cắt dọc** thang bộ (cắt song song stair path, LA–LC): chiều cao vế `169.4mm x 16R = 2710 (EQUAL RISERS)`, cao độ tầng, chiều dài vế, thông thuỷ dưới chiếu nghỉ, tay vịn; tag vế, P01, cao độ, F../C.., cửa; số bậc liên tục |
| `drafting-stair-section-cross` | **Mặt cắt ngang** thang bộ (cắt không song song stair path, vế nhìn thẳng đầu, XA–XC): chuỗi chiều cao từng dải vế có công thức, cao độ tầng, chuỗi thông thuỷ | dày chiếu nghỉ liên tục, bề rộng tới tay vịn, tường; tag vế chữ đứng hai bên, tay vịn, cao độ, F../C..; số bậc Start and End trên vế thấy cổ bậc |
| `drafting-stair-plan` | **Mặt bằng** lõi thang bộ (SA1–SD; mặt cắt thang sẽ là skill riêng): 3 vế + mọi chiếu nghỉ thấy được (kể cả dưới View Depth), dim tay vịn không cần dim tay, dim thông thuỷ (CLEAR chỉ trên bề rộng vế; mép lan can → mép bậc → tường), chiều dài vế có công thức (bậc trên cùng ngang chiếu nghỉ không tính), chuỗi chiếu nghỉ / tổng khép tới tường, tường/cửa tới trục; tag vế, tay vịn, cao độ, cửa, hoàn thiện; đếm/đánh số bậc từng vế; stair path chỉ có mũi tên |
| `drafting-plan-dims` | **Khai triển mặt bằng kích thước**: thẻ Dim cửa tới tường, trục (PD1–PD6: chuỗi trục → mép tường → mép cửa → … → trục, mỗi cửa vẽ ra đều có bề rộng và khoảng cách), Tag cửa (PT1–PT6: đúng 1 tag, sát cửa, phía không có cung mở / cửa sổ phía ngoài, không đè, leader vuông góc), Tag room (TR1–TR8: leader ngắn nhất, trong room nếu đủ chỗ; chấm điểm 0–10, 0 khi trên tường / cạnh sàn, leader trùng leader / tường / qua chấm leader khác, −3 mỗi room leader đi qua, thiếu tag thì thẻ 0), Tag cao độ sàn (FL1–FL3: mỗi gian phòng một cao độ, trừ phòng nhỏ nằm trong phòng khác; buồng thang có cao độ trong và ngoài cửa; type theo người dùng), Tag tường (WT1–WT5: type tag có sẵn, chưa có thì hỏi; thẳng, sát tường, không đè tường; bỏ vùng vệ sinh; phải có leader), Ký hiệu độ dốc (SL1–SL5: mái, sàn chênh cao độ, ramp; Spot Slope type hướng xuống, đơn vị thập phân hoặc %; type có sẵn) |
| `drafting-ceiling-plan` | **Khai triển mặt bằng trần**: thẻ Tag room (TR1–TR8, như trên; cạnh trần thay cạnh sàn) |
| `drafting-plan-floor-finish` | **Khai triển mặt bằng hoàn thiện sàn** (cùng cấp với `drafting-plan-dims`): thẻ Tag cao độ sàn (FL1–FL3) và Ký hiệu độ dốc (SL1–SL5), chung với `drafting-plan-dims`; thẻ mã hoàn thiện sàn sẽ thêm |
| `drafting-tags-titles` | Room tag (sửa tag ngoài phòng, tag "?"), view title |
| `drafting-views-sheets` | Viewport, crop/scope box, đầu trục 2D, view template, link |
| `drafting-sheet-layout` | **Lập bộ hồ sơ** theo loại công trình (nhà liên kế, biệt thự, nhà cao tầng, sân bay, công cộng, công nghiệp): loại + giai đoạn → **sổ bộ hồ sơ** (mỗi bản vẽ một dòng, trạng thái planned → sheet → view → placed → checked) → đối chiếu model → tỷ lệ, vừa khổ, chia vùng → kiểm layout LO1–LO12, Z1–Z5 (5 thẻ) |
| `drafting-sheet-create` | **Khởi tạo sheet** từ sổ: gom bản vẽ thành sheet, đánh số, đặt tên theo danh mục điển hình, kiểm khung tên, tạo qua danh mục bản vẽ (print set + No. tự theo), tham số sheet, nhóm Project Browser (6 thẻ, SC1–SC10) |
| `drafting-view-create` | **Tạo view cho sheet** từ sổ: mỗi bản vẽ → loại view Revit (mặt bằng, duplicate, dependent theo vùng, mặt đứng, mặt cắt, callout thang / WC, interior elevation, chi tiết, legend, schedule), template, tỷ lệ, crop, tên; dùng lại view có sẵn (8 thẻ, VC1–VC12) |
| `drafting-sheet-place` | **Bỏ view vào sheet** từ sổ: kiểm sẵn sàng, bảng bố trí mm trên giấy, phần cố định (ghi chú, legend, key plan, mũi tên Bắc, schedule), đặt view, căn chuỗi mặt bằng theo điểm trục và mặt đứng / mặt cắt theo ±0.000, kiểm (6 thẻ, VP1–VP10) |
| `drafting-visual-check` | Xuất ảnh sheet và soát lỗi trình bày |

### Thẻ bước (Step cards) — chia để trị

Skill nhiều bước thì mỗi bước là **một file riêng** trong `skills/<skill>/steps/`, gọi là **thẻ bước**: `NN-<mục>.md`, gồm làm gì (lệnh) · xong khi · chỉ bỏ khi · không được làm. `SKILL.md` chỉ còn phạm vi, đầu vào từ profile, danh sách thẻ theo thứ tự và các điều cấm chung. Ghi chú dùng chung cho nhiều thẻ: `steps/_<chủ đề>-common.md`.

- Claude chỉ mở thẻ của bước đang làm (ít token).
- Duyệt, sửa một bước = sửa một file trên GitHub; lịch sử (commit, diff) theo từng bước.
- Quy trình chỉ gọi skill; thứ tự thẻ nằm trong `SKILL.md`, "xong khi" / "chỉ bỏ khi" chỉ nằm trong thẻ.

Đã tách: `drafting-stair-plan`, `drafting-stair-section-parallel`, `drafting-stair-section-cross` (mỗi skill 16 thẻ), `drafting-views-sheets` (8), `drafting-grid-dims` (4), `drafting-opening-dims` (4), `drafting-opening-tags` (6), `drafting-tags-titles` (2), `drafting-plan-dims` (6), `drafting-plan-floor-finish` (2), `drafting-ceiling-plan` (1), `drafting-section-general` (1), `drafting-sheet-layout` (5), `drafting-sheet-create` (6), `drafting-view-create` (8), `drafting-sheet-place` (6). Skill một bước (`drafting-dims`) và các skill mở/đóng phiên (`drafting-start`, `drafting-end`, `drafting-visual-check`, `drafting-review-round`) giữ nguyên.

### Quy trình (Workflows)

Quy trình là lớp trên skill: nó **chỉ gọi skill** — skill nào, theo thứ tự nào, cho view nào, chạy thẻ nào (mặc định tất cả) và vì sao thứ tự đó. Các bước, "xong khi", "chỉ bỏ khi" nằm trong skill và thẻ bước, không chép vào quy trình. Đầu quy trình (bước 0) gom **mọi view / sheet** của công việc một lần — đưa sheet thì lấy mọi view trên sheet — rồi mỗi bước làm **một lượt** cho tất cả view (lệnh nhận `viewIds`: một lần gọi cho mỗi đợt ≤ 10 view; bước theo sheet: mọi sheet trong một lần gọi) rồi mới sang bước sau (vd. lõi thang: tag vế cho mọi mặt bằng → dim thông thuỷ cho mọi mặt bằng → …). Skill `drafting-workflow` chạy quy trình và giữ checklist `workflow-<ngày>-<tên>.md` (trong thư mục đợt comment, hoặc `log/`): mỗi ô bước × view là `x` xong, `-` bỏ (có lý do), `!` chưa xong (có lý do) hoặc `n/a`. Báo cáo cuối không được còn ô trống.

| Quy trình | Gọi skill (thẻ) theo thứ tự |
|---|---|
| `elevation-section` | start (gom view / sheet) → views-sheets (00–04) → section-general (fill kết cấu, chỉ mặt cắt) → grid-dims → dims (cao độ) → opening-dims → opening-tags → tags-titles (title) → views-sheets (07) → visual-check → end |
| `floor-plan` | start (gom view / sheet) → views-sheets (00–04) → grid-dims → plan-dims (dim cửa → tag cửa → tag room → cao độ sàn → tag tường → độ dốc) → plan-floor-finish (cao độ sàn, độ dốc, mặt bằng hoàn thiện) → tags-titles (room tag, title) → views-sheets (07) → visual-check → end |
| `ceiling-plan` | start (gom view / sheet) → views-sheets (00–04) → grid-dims → ceiling-plan (tag room) → tags-titles (room tag, title) → views-sheets (07) → visual-check → end |
| `stair-core` | start (gom view / sheet) → stair-plan → stair-section-parallel → stair-section-cross → visual-check → end |
| `sheet-layout` | start (gom mọi sheet của bộ) → sheet-layout (loại công trình → sổ bộ hồ sơ → đối chiếu model → tỷ lệ, chia vùng) → sheet-create (khởi tạo sheet) → view-create (tạo view) → views-sheets (00, 02, 03: crop, đầu trục) → sheet-place (bỏ view vào sheet) → tags-titles (title) → sheet-layout (kiểm layout) → views-sheets (07) → visual-check → end |
| `review-round` | review-round (đọc + phân loại) → start (gom view / sheet mọi task) → skill theo chủ đề (task cùng chủ đề làm một lượt) (thẻ khảo sát, thẻ của comment, thẻ kiểm lại) → tags-titles (title) → visual-check → end |

Thêm quy trình mới: một file `workflows/<tên>.md` (dòng 0 = `drafting-start` gom view / sheet; bảng skill · thẻ · view + vì sao thứ tự này) + một dòng trong bảng của `drafting-workflow` + một dòng ở bảng trên.

### Domain

| File | Nội dung |
|---|---|
| `drafting-work-rules.md` | Luật cứng, nhịp làm việc, làm theo đợt + type đánh dấu (§2c), cách báo cáo |
| `drafting-grid-dims.md` | 5 quy tắc dim trục (G1–G5): nhóm trục, 1 chain + 1 overall, dải giữa crop và bubble, annotation crop, bubble không bị che |
| `drafting-stair-section-parallel.md` | Mặt cắt dọc thang bộ: vế F1…, công thức R/T, lớp dim, tag, số bậc (LA–LC) |
| `drafting-stair-section-cross.md` | Mặt cắt ngang thang bộ: dải vế, hướng vế (away / toward), lớp dim, chuỗi thông thuỷ liên tục, tag, số bậc Start and End (XA–XC) |
| `drafting-stair-plan.md` | Mặt bằng lõi thang bộ: 3 vế V1/V2/V3, lớp dim, mép thông thuỷ, CLEAR chỉ cho bề rộng vế, công thức chiều dài vế, tag, đếm bậc, stair path (SA1–SD) |
| `drafting-dimensions.md` | Chuẩn dim cao độ, type dim (dim trục → `drafting-grid-dims.md`) |
| `drafting-opening-dims.md` | 5 quy tắc dim cửa sổ, cửa đi (Q1–Q5) |
| `drafting-opening-tags.md` | 8 quy tắc tag cửa sổ, cửa đi (T1–T8) |
| `drafting-section-general.md` | Mặt cắt chung: fill kết cấu bị cắt (SF1–SF10) |
| `drafting-annotation.md` | Room tag (đặt và chấm điểm tag TR1–TR8, sửa tag), cao độ sàn trên mặt bằng (FL1–FL3), ký hiệu độ dốc (SL1–SL5), view title, leader vuông góc |
| `drafting-plan-dims.md` | Mặt bằng kích thước: dim cửa tới tường, trục (PD1–PD6), tag cửa (PT1–PT6), tag room, cao độ sàn, tag tường (WT1–WT5), ký hiệu độ dốc |
| `drafting-ceiling-plan.md` | Mặt bằng trần: các thẻ bước (hiện có tag room) |
| `drafting-plan-floor-finish.md` | Mặt bằng hoàn thiện sàn: các thẻ bước (hiện có cao độ sàn) |
| `drafting-views-sheets.md` | Viewport, crop, dependent view, template |
| `drafting-sheet-layout.md` | Layout bộ hồ sơ: loại công trình → file bộ hồ sơ, giai đoạn (YT / CS / KT / TC / HC), nhóm và đánh số bản vẽ, bộ hồ sơ lõi, khổ giấy, tỷ lệ, bố cục trên sheet (LO1–LO12), chia vùng + key plan + matchline (Z1–Z5), tên sheet, **sổ bộ hồ sơ** (§11) |
| `drafting-sheet-create.md` | Khởi tạo sheet từ sổ (SC1–SC10): gom sheet, số, tên, khung tên, tạo qua danh mục, tham số sheet |
| `drafting-view-create.md` | Tạo view cho sheet (VC1–VC12): bảng bản vẽ → loại view Revit, tạo từ đâu, template, tỷ lệ, crop, tên; hợp đồng lệnh `view_create` (chưa có) |
| `drafting-sheet-place.md` | Đặt view lên sheet (VP1–VP10): bảng bố trí mm, thứ tự đặt, căn theo điểm trục, legend / schedule; hợp đồng lệnh `viewport_place` (chưa có) |
| `drafting-sheet-set-<loại>.md` | Bộ bản vẽ riêng của từng loại công trình (cộng vào bộ lõi): `townhouse` nhà liên kế, `villa` biệt thự, `highrise` nhà cao tầng, `airport` sân bay, `public` trường học / bệnh viện / TTTM, `industrial` nhà xưởng / kho. Mỗi job chỉ mở một file |
| `drafting-tools.md` | Danh mục lệnh MCP theo chủ đề |
| `drafting-api-pitfalls.md` | Bẫy Revit API và cách tránh |

**Chỉ đọc file cần dùng**: Claude không đọc hết repo. `drafting-session` có bảng việc → skill → domain; mỗi skill chỉ mở đúng một file chuẩn của nó. `drafting-tools.md` và `drafting-api-pitfalls.md` chỉ tra (search) dòng cần. Khi làm việc ngay trong repo, `CLAUDE.md` nhắc lại quy tắc này.

## Yêu cầu (Requirements)

- Claude Code.
- Revit 2023, 2024 hoặc 2025. Add-in build được cho 2023–2027; các lệnh trong `revit-commands/` và script Python còn dùng `ElementId.IntegerValue` (Revit 2026 đã bỏ), nên 2026+ cần đổi sang `ElementId.Value` trước (`addin/tests/CommandCheck` chỉ ra từng chỗ). Lệnh viết và chạy thử trên Revit 2023.
- Để build add-in: .NET SDK 8 (`winget install Microsoft.DotNet.SDK.8`; Revit 2027 cần SDK 10). Không cần pyRevit, không cần Node (Node chỉ cho web app tuỳ chọn), không cần Revit MCP nào khác.

## Cài đặt (Install)

### Cách dễ: file cài đặt `.exe`

1. Tải **[ClaudeUsingRevit-Setup.exe](../../releases/download/setup-latest/ClaudeUsingRevit-Setup.exe)** (trang *Releases* của repo, mục *setup-latest*; GitHub tự build lại sau mỗi lần cập nhật).
2. Đóng Revit và Claude. Bấm đúp file vừa tải. Windows có thể báo *"Windows protected your PC"* (file chưa ký số): bấm **More info** → **Run anyway**.
3. Tick các bản Revit (tự tìm sẵn) → **Cài đặt**. Không cần quyền quản trị, không cần cài .NET, pyRevit hay Node.
4. Mở Revit: tab **claude using revit**, nút đầu tiên của panel **Claude** cho biết đã kết nối với Claude chưa. Lần đầu Revit hỏi có tải add-in không: chọn **Always Load**.

Bộ cài làm đủ việc của `install.ps1 -AddIn -RegisterClaude` (add-in, các nút, lệnh, skill cho Claude, nối Claude Code; sao lưu `~/.claude.json` trước khi sửa) và thêm mục **claude using revit** vào *Settings > Apps* của Windows để gỡ. Cài lại bản mới: chạy file mới, bấm Cài đặt (cài đặt riêng được giữ).

### Cách cho người phát triển (từ repo)

Đóng mọi cửa sổ Revit, rồi:

```powershell
git clone <repo-url>
cd claude-using-revit
powershell -ExecutionPolicy Bypass -File install.ps1 -AddIn -RegisterClaude
```

Lệnh trên:
- copy `skills/*` vào `~/.claude/skills/`, `workflows/` vào `~/.claude/drafting-domain/workflows/`, `agents/*.md` vào `~/.claude/agents/`, `domain/`, `templates/`, `tools/` vào `~/.claude/drafting-domain/`;
- `-AddIn`: build add-in claude using revit (`addin/build.ps1`) cho các bản Revit có trên máy (hoặc `-Revit 2023,2024`) và cài vào `%APPDATA%\claude-using-revit\bin\<bản>\` + file `ClaudeUsingRevit.addin` ở `%APPDATA%\Autodesk\Revit\Addins\<bản>\`; các nút vào `%APPDATA%\claude-using-revit\extension\`, lệnh `revit-commands/*.cs` vào `%APPDATA%\claude-using-revit\commands\`. Xoá extension pyRevit cũ `ClaudeUsingRevit.extension` nếu còn (không thì có hai tab); cài đặt mà các nút đã nhớ dưới pyRevit được đọc lại ở lần dùng đầu. Highlight có type `Highlight - Red 50%` và dấu Extensible Storage ghi tên người tạo, nên nút Xoá vùng tô không bao giờ xoá Filled Region khác hay highlight của người khác;
- `-Dist <thư mục>` (thêm vào `-AddIn`): cài bản đã build sẵn (`<thư mục>\<bản Revit>\ClaudeUsingRevit.dll`) thay vì tự build;
- `-Dev` (thêm vào `-AddIn`): add-in đọc thẳng `addin/extension` và `revit-commands` của repo này: sửa script / lệnh là có hiệu lực ở lần bấm / lần gọi sau, không cần cài lại;
- `-RegisterClaude`: thêm máy chủ MCP vào Claude Code: `claude mcp add --transport http --scope user claude-using-revit http://127.0.0.1:8970/mcp` (cấu hình `revit-mcp` cũ thì xoá: `claude mcp remove revit-mcp`);
- `-WebApp`: tuỳ chọn, máy chủ web (trình duyệt, iPad trong LAN), vào `%APPDATA%\claude-using-revit\webapp` (cần Node). Panel trong Revit không cần nó. Chạy từ Terminal thường, không từ phiên Claude desktop (gói MSIX: thư mục mới dưới `%APPDATA%` nằm trong kho riêng của app, Revit không thấy).

Mở Revit: tab **claude using revit**, panel **Claude** hiện trạng thái kết nối. Mở phiên Claude Code mới, gọi `hello_revit` hoặc `cur_status` để thử. Cài đặt của add-in: `%APPDATA%\claude-using-revit\config.json` (`port` 8970, `autoStart`, `extensionDir`, `commandsDir`); log: `%APPDATA%\claude-using-revit\logs\`.

**Phát triển add-in**: bộ cài `.exe`: `bash addin/setup/make-setup.sh` (+ `bash addin/tests/setup-test.sh`); `addin/build.ps1 -Check` (build + biên dịch thử mọi lệnh theo API của từng bản Revit), `bash addin/tests/smoke.sh` (máy chủ MCP không cần Revit, kèm client MCP chính thức). Nút mới = thư mục `*.pushbutton` mới (cần mở lại Revit); lệnh mới = file `.cs` mới (Claude thấy ngay).

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
| Tool có sẵn của máy chủ MCP | add-in cũ 192 tool (~36.6k token nếu nạp sẵn); claude using revit 3 tool | **A**: add-in riêng, ít tool có sẵn | đã làm |
| Lệnh của repo (header `mcp-tool`) | ~11.8k token nếu nạp sẵn; chỉ tên (~0.8k) khi tool search bật; mỗi lệnh đã nạp ~140 token | **B**: header ngắn, giải thích ở `// ---- Details` | đã làm |
| Tool search của Claude Code | — | **C**: giữ bật (mặc định), kiểm tra bằng `/context` | kiểm tra trên máy |
| Nạp lệnh từng cái một (mỗi lần một lượt) | 1 lượt / lệnh | **D**: dòng **Tools** trong mỗi skill → nạp hết trong 1 lần ToolSearch | đã làm |
| Mô tả skill + subagent (gửi mỗi request, cả phiên không làm Revit) | 9.5k → 8.6k ký tự cho 23 skill; mỗi mô tả ≤ 500 | mô tả ngắn + từ khoá gọi, luật nằm trong thân skill | đã làm, CI kiểm (`budget_check.py`) |
| Chuẩn dài (`drafting-stair-plan.md` ~28k ký tự, mặt cắt thang ~25k, …) bị đọc cả file | ~10k token cho chuẩn dài nhất (ước tính) | **G**: đọc theo mục: dàn ý (`grep -n '^#'`) + phần mở đầu + đúng mục luật mà thẻ bước nêu | đã làm (`drafting-session`, "Standards by section") |
| Nội dung lặp giữa `drafting-session`, `drafting-start`, `drafting-work-rules.md`, mục Score của 106 thẻ bước | mỗi phiên / mỗi thẻ | **H**: nói một lần, chỗ khác chỉ trỏ tới § | đã làm |
| Ảnh kiểm tra (mỗi ảnh ~1.5–2k token, ở lại trong hội thoại) | theo số ảnh | **E**: subagent `drafting-visual-checker` (Sonnet) đọc ảnh, chỉ trả chữ | đã làm |
| Kết quả lệnh | theo lệnh | **F**: mặc định ngắn (số lượng + lỗi), chi tiết sau cờ `verbose`/`detail` | luật cho lệnh mới (`CLAUDE.md`) |

### A — Ít tool có sẵn

Add-in Revit MCP cũ đăng ký sẵn 192 tool (~36.6k token khi nạp sẵn), nên cần profile `drafting`. claude using revit chỉ có 3 tool có sẵn (`cur_task`, `cur_status`, `run_dynamic_command`) cùng các lệnh của repo: không còn gì để cắt.

### C — Tool search (Claude Code)

Claude Code bản mới **mặc định** hoãn nạp tool MCP: đầu phiên chỉ gửi tên tool, schema chỉ nạp khi Claude tìm (ToolSearch). Nó tự tắt (nạp sẵn mọi tool, ~48k token mỗi bước) khi:
- `ANTHROPIC_BASE_URL` trỏ tới proxy không phải của Anthropic → đặt `"ENABLE_TOOL_SEARCH": "true"` trong `env` của `~/.claude/settings.json` (chỉ khi proxy hỗ trợ `tool_reference`);
- có `ENABLE_TOOL_SEARCH=false` hoặc `CLAUDE_CODE_DISABLE_EXPERIMENTAL_BETAS`;
- model cũ hơn thế hệ 4.5.

Kiểm tra: gõ `/context` trong phiên có máy chủ `claude-using-revit`. Tool MCP phải hiện là deferred (hoãn), không chiếm hàng chục nghìn token. Nếu cấu hình `claude-using-revit` có `"alwaysLoad": true` thì bỏ đi. Cập nhật Claude Code (`claude update`) nếu bản cũ.

Dự phòng khi không bật được tool search: mọi lệnh cũng gọi được qua một tool `run_dynamic_command {name, args}`; có thể cho claude using revit chỉ đăng ký tool đó thay vì mỗi lệnh một tool (`addin/src/ClaudeUsingRevit/Mcp/ToolRegistry.cs`); Claude tra tham số trong `drafting-tools.md` và header `.cs`.

### G, H — Đọc đúng phần cần, không lặp

- Chuẩn trên ~120 dòng không đọc cả file: thẻ bước nêu mã luật (`SB1`, `G3`…), Claude đọc dàn ý, phần mở đầu (cách đọc view, bảng tóm tắt luật), "Mẫu tham chiếu", rồi chỉ mục của luật đang làm. "Bài học" thì `grep` theo triệu chứng. Vì vậy mỗi luật giữ một heading riêng trong chuẩn.
- Điều đã có trong `drafting-work-rules.md` (đọc đầu mỗi phiên) không chép lại trong skill hay thẻ bước.
- `python addin/tests/budget_check.py` kiểm: mô tả skill ≤ 500 ký tự, mô tả lệnh ≤ 250, mô tả tham số ≤ 80 và không bị cắt cụt (`…`), tên lệnh trên dòng **Tools** có thật (tên sai = một lượt ToolSearch vô ích). CI chạy ở mỗi lần push.

### Cách làm việc ít token

- **Model**: việc đã có skill (chạy lại quy trình quen) → `/model sonnet`; chủ đề mới, viết lệnh C# mới → Opus.
- **Một chủ đề một phiên**: đổi chủ đề → `/clear`. Trạng thái nằm ở `TASKS.md`, `log/`, `drafting-profile.md`, không cần kéo hội thoại dài rồi `/compact`.
- **Lệnh một phát**: mỗi chủ đề nên có một lệnh làm cả khảo sát → áp dụng → kiểm tra (như `stair_plan_annotate`, `grid_dims_band`), mỗi view chỉ còn vài lần gọi: preview, apply, kiểm tra.
- **Chạy hàng loạt không cần ngồi canh** (máy có Revit đang mở, từng phiên một vì Revit API chỉ chạy một luồng):
  ```powershell
  claude -p "Dùng skill drafting-grid-dims cho các view <danh sách>. Ghi log, báo cáo Việc tồn." --model sonnet --allowedTools "mcp__claude-using-revit" "Read" "Edit" "Write" "Grep" "Glob" "Agent"
  ```
  `mcp__claude-using-revit` cho phép mọi tool của máy chủ `claude-using-revit`. Sáng hôm sau xem log và ảnh.

## Cập nhật bài học (Keeping it alive)

Mỗi lần user sửa lại kết quả của Claude, ghi bài học thành **một dòng có ngày**:

| Loại bài học | Ghi vào đâu |
|---|---|
| Đúng cho mọi dự án | file `domain/` tương ứng → commit lên repo này |
| Chỉ đúng cho dự án đang làm | `drafting-profile.md` của dự án đó (không đưa lên repo) |

Bài học đã được một lệnh tự bảo đảm, hoặc đã gộp vào một quy tắc của chuẩn → xoá dòng bài học đó (chuẩn càng ngắn, mỗi việc càng ít token).

Không đưa tên khách hàng, tên model, số sheet hay đường dẫn máy vào repo này.
