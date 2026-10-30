# Web app "claude using revit"

Giao diện web (phong cách "Quét Lidar") cho mọi công cụ của tab pyRevit **claude using revit** và mọi lệnh Revit MCP, mở được trên máy tính hay iPad cùng mạng.

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
- **Phong cách "Quét Lidar":** nền đen chấm như đám mây điểm, mỗi công cụ là một tấm mica trong suốt không màu (cạnh sáng, có độ dày, bóng đổ) nằm trên mặt bàn 3D nghiêng; chạm một tấm thì nó nổi lên phẳng, đủ nút để thao tác (Esc / ✕ để trả về); nút Phẳng / 3D đổi kiểu xem (màn hẹp mặc định Phẳng). Chữ Unbounded / JetBrains Mono. Màu chỉ dành cho việc cần làm: task `status_board` (chỉ đọc) cho biết từng công cụ còn gì; tấm còn việc sáng cạnh cam laser, nổi cao hơn mặt bàn, có tia laser quét qua và số việc, ô đếm "việc còn" ở đầu trang sáng cam; thẻ đã xong đơn sắc với `[ OK ]`; thẻ chạy khi bấm (Xuất, 3D) mờ, nét đứt. Quét lại khi đổi model / view và sau mỗi lệnh ghi.
- **Màn hình:** *Công cụ* (một thẻ cho mỗi nhóm nút ribbon, Xuất PDF và DWG chung một danh sách sheet), *Tất cả lệnh* (task, lệnh claude-using-revit, lệnh dynamic khác, lệnh gốc MCP; form tự sinh từ `inputSchema`), *Nhật ký* (200 việc gần nhất, kết quả JSON).
- Log máy chủ: `%APPDATA%\claude-using-revit\webapp.log`. Thử máy chủ từ repo: `node webapp/server.mjs` (đặt `CUR_WEBAPP_HOST=127.0.0.1` để chỉ mở trong máy).
