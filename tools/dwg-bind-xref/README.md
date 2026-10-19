# dwg-bind-xref

Gộp (bind) mọi external reference của một file DWG thành **một file DWG độc lập**, bằng AutoCAD `accoreconsole` (chạy ngầm, không mở AutoCAD). Dùng cho DWG xuất từ Revit kèm xref (sheet → view → link).

- File gốc và các file xref **không bị sửa**. Kết quả nằm cạnh file gốc: `<tên><Suffix>.dwg` (mặc định `_merged`), định dạng AutoCAD 2018.
- Bind kiểu **Insert** (mặc định): layer giữ tên sạch, không có tiền tố `xref$0$`. `-BindType Bind` nếu cần giữ tiền tố.
- Bind lặp tới khi hết xref lồng nhau (tối đa 6 lượt). Xref không tìm thấy file thì không bind được: thông báo ghi `CHUA HET` kèm tên.
- Log mỗi lần chạy: `%LOCALAPPDATA%\DwgBindXref\logs`.

## Dùng

Chuột phải file `.dwg` → **Bind Xref -> _merged.dwg** (Windows 11: trong *Show more options*). Xong hiện hộp thông báo kết quả.

Dòng lệnh:

```powershell
.\bind-xref.ps1 'D:\a\sheet.dwg','D:\b\sheet2.dwg' -NoPause
```

Tham số: `-Suffix _merged` · `-BindType Insert|Bind` · `-Format 2018` · `-NoPause` (không hiện hộp thông báo). Biến môi trường `ACCORECONSOLE` để chỉ đường dẫn khác; mặc định lấy AutoCAD mới nhất trong `Program Files\Autodesk`.

## Cài menu chuột phải

```powershell
.\install-menu.ps1             # chép script vào %USERPROFILE%\Tools\DwgBindXref + menu HKCU (không cần admin)
.\install-menu.ps1 -Uninstall  # gỡ menu
```

Hoặc `install.ps1 -DwgBindXref` ở gốc repo.
