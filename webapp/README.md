# Web app "claude using revit"

Giao diện web (kiểu Apple Vision, nền vũ trụ) cho mọi công cụ của tab pyRevit **claude using revit** và mọi lệnh Revit MCP, mở được trên máy tính hay iPad cùng mạng.

```
Trình duyệt (PC, iPad)  ──HTTP 8780, mã truy cập──>  webapp/server.mjs (Node, không thư viện)
                                                        │  đọc %APPDATA%\claude-using-revit\web-bridge\<pid>.json
                                                        ▼
                         mỗi cửa sổ Revit: lib/web_bridge.py (pyRevit, 127.0.0.1:48990+, có khoá)
                                                        │  ExternalEvent, từng việc một
                                                        ▼
                    lib/tasks.py (code của các nút)  ·  add-in Revit MCP trong cùng tiến trình
                    (lệnh dynamic revit-commands/ + dynamic-commands/, lệnh gốc của add-in)
```

- **Không dùng cổng 8964.** Cổng MCP chỉ cho một client, nên web app có cầu nối riêng trong từng cửa sổ Revit và không bao giờ tranh cổng với Claude.
- **Cầu nối mỏng:** `web_bridge.py` chỉ nhận việc, chạy trên luồng Revit và trả JSON. Logic nằm ở `tasks.py` (dùng chung với nút ribbon và `pyrevit_task`) và ở add-in MCP (`CommandExecutor`, `run_dynamic_command`).
- **Khởi động:** `startup.py` của extension bật cầu nối mỗi lần pyRevit load (Reload thì thay cầu nối cũ trên cùng cổng) và bật máy chủ web nếu chưa chạy. `install.ps1 -WebApp` cài và khởi động lại máy chủ web.
- **Truy cập:** `http://localhost:8780/?k=<mã>` một lần cho mỗi thiết bị (cookie SameSite=Strict). Mã và cài đặt nằm ở `%APPDATA%\claude-using-revit\webapp.json`: `host` `127.0.0.1` (mặc định, chỉ máy này) hoặc `0.0.0.0` (cả LAN / Tailscale), `port`, `token`, `commandsDir`, `mcpBuildDir`. Nút **iPad / LAN** trên trang hiện link cho thiết bị khác.
- **An toàn:** mọi `/api` cần mã; Host phải là máy này (chống DNS rebinding); POST khác nguồn bị chặn; cầu nối chỉ nghe 127.0.0.1 và cần khoá trong file mà trang web không đọc được. Lệnh có thể sửa model hoặc ghi file luôn hỏi lại kèm tên model.
- **Phong cách kiểu Apple Vision:** nền vũ trụ vẽ bằng canvas (sao, tinh vân, dải Ngân Hà, thiên hà có vành đai; vẽ một lần, `public/space.js`), cửa sổ kính mờ không màu, thanh nổi trên (model · view · số việc còn · kiểm tra lại · iPad) và thanh nổi bên trái (Công cụ, Lệnh, Nhật ký; điện thoại: ở dưới). Mỗi công cụ là một ô; chạm thì mở thành cửa sổ riêng chỉ với các nút cần thiết. Màu (cam hệ thống) chỉ dành cho việc còn làm, theo task chỉ đọc `status_board`: ô còn việc viền và chữ cam + số việc; ô đã xong có dấu ✓; các phần khác (checkbox, công tắc) đều không màu.
- **Màn hình:** *Công cụ* (một thẻ cho mỗi nhóm nút ribbon, Xuất PDF và DWG chung một danh sách sheet), *Tất cả lệnh* (task, lệnh claude-using-revit, lệnh dynamic khác, lệnh gốc MCP; form tự sinh từ `inputSchema`), *Nhật ký* (200 việc gần nhất, kết quả JSON).
- **Ứng dụng trên máy** (`desktop/app.ps1`, lối tắt Start Menu "claude using revit"): web app trong cửa sổ riêng (WebView2, có sẵn trên Windows 11), có biểu tượng trên taskbar, nhớ kích thước và vị trí, mở lại thì đưa cửa sổ đang mở lên trước. DLL WebView2 SDK không nằm trong repo: `install.ps1 -WebApp` chép từ một phần mềm đã cài trên máy (loader x64).
- Log máy chủ: `%APPDATA%\claude-using-revit\webapp.log`. Thử máy chủ từ repo: `node webapp/server.mjs` (đặt `CUR_WEBAPP_HOST=127.0.0.1` để chỉ mở trong máy).
