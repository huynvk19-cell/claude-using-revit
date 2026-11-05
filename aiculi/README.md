# AI Culi (add-in Revit)

Một add-in, ba vai trò: **tab công cụ** của người dùng, **máy chủ MCP** cho Claude, **thanh trạng thái** kết nối. Không cần pyRevit, không cần Revit MCP nào khác, không cần Node.

```
Claude Code ──HTTP──> http://127.0.0.1:8970/mcp ──> AiCuli.dll (trong Revit)
                                                     ├─ Mcp/McpServer      JSON-RPC (MCP Streamable HTTP), cổng chung cho mọi cửa sổ Revit
                                                     ├─ Mcp/ToolRegistry   tool = mỗi file revit-commands/*.cs + culi_task, culi_status, run_dynamic_command
                                                     ├─ Mcp/DynamicCommands biên dịch .cs (roslyn/, Roslyn) lần gọi đầu và khi file đổi
                                                     ├─ Core/RevitQueue    một ExternalEvent, chạy từng việc trên luồng API
                                                     ├─ Python/PyHost      IronPython 2.7: startup.py, hook, mỗi lần bấm nút, culi_task
                                                     └─ Ribbon + Ui        tab "AI Culi" dựng từ extension/, panel Claude (trạng thái, Tải lại)
Người dùng ──bấm nút──> extension/AI Culi.tab/<panel>.panel/<nút>.pushbutton/script.py  (from culi import forms, revit, script ...)
```

| Thư mục | Nội dung |
|---|---|
| `src/AiCuli/` | Add-in C#. Một DLL cho mỗi bản Revit: `-p:RevitVersion=2023…2027` (net48 cho 2023–2024, net8 cho 2025–2026, net10 cho 2027) |
| `src/AiCuli.Compiler/` | Roslyn tách riêng (strings vào, bytes ra), nạp từ `roslyn/`; .NET 8+ thì trong AssemblyLoadContext riêng nên không đụng Roslyn của add-in khác |
| `extension/` | Phần Python: nút ribbon, `lib/tasks.py` (task dùng chung), `lib/culi/` (API của host thay cho pyRevit: `HOST_APP`, `EXEC_PARAMS`, `forms`, `revit`, `script`), `startup.py`, `hooks/` |
| `icons/` | Vẽ icon nút (`make_icons.ps1`) và icon app |
| `tests/CommandCheck/` | Biên dịch mọi `revit-commands/*.cs` theo API của một bản Revit, không cần Revit; kiểm tra luật header |
| `tests/McpSmoke/`, `tests/smoke.sh` | Máy chủ MCP thật với vài kiểu Revit giả: tự kiểm tra, hai "cửa sổ" chuyển cổng, client MCP chính thức (TypeScript SDK) |
| `build.ps1` | Build mọi bản Revit có trên máy vào `dist/<bản>/`; `-Check` chạy thêm CommandCheck |

**Trạng thái** (nút đầu tiên của tab, cập nhật mỗi giây): xanh lá *Đã kết nối* / *Đang chạy* · xanh dương *Chờ kết nối* (cửa sổ này giữ cổng) · vàng *Cửa sổ khác* (cửa sổ khác giữ cổng; cửa sổ này nhận khi cổng rảnh) · xám *Đã tắt* · đỏ *Lỗi*. Bấm vào: cửa sổ chi tiết (client, lệnh gần đây, bật / tắt, *Nhận kết nối về cửa sổ này*, chép lệnh `claude mcp add`, mở log).

**Dữ liệu** `%APPDATA%\AiCuli\`: `config.json` (`port`, `autoStart`, `extensionDir`, `commandsDir`, `tabName`), `bin\<bản>\`, `extension\`, `commands\`, `logs\` (C#: `aiculi-<ngày>.log`, Python: `python.log`), `instances\<pid>.json` (vai trò từng cửa sổ), `open-docs\`, `web-bridge\`, `tool-config\` (cài đặt từng nút), `control.key`.

**Phát triển**
- `install.ps1 -AddIn -Dev`: add-in đọc thẳng `extension/` và `revit-commands/` của repo; sửa là chạy, không cài lại. Chỉ sửa C# trong `src/` mới cần build + cài lại (đóng Revit).
- Trước khi đẩy: `build.ps1 -Check` (hoặc trên Linux / CI: `dotnet build src/AiCuli -c Release -p:RevitVersion=2025` rồi `dotnet run --project tests/CommandCheck -c Release -- 2025`), `bash tests/smoke.sh` khi sửa máy chủ MCP.
- GitHub Actions (`.github/workflows/aiculi.yml`) làm cả hai ở mỗi lần push và để lại bản build *aiculi-dist* (cài bằng `install.ps1 -AddIn -Dist <thư mục>`).
- Revit 2026+: lệnh và script còn dùng `ElementId.IntegerValue` (đã bị bỏ) → đổi sang `ElementId.Value`; CommandCheck 2026 chỉ ra từng dòng.
