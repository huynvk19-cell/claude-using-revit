# Add "Bind Xref -> _merged.dwg" to the Windows Explorer right-click menu of .dwg files, for the current user only (no admin).
#   install-menu.ps1 [-Target <folder>] [-Uninstall]
# Copies bind-xref.ps1 to <Target> (default %USERPROFILE%\Tools\DwgBindXref) and registers the menu entry under HKCU.
# Windows 11: the entry is under "Show more options" (Shift+F10 / Shift+right-click).
param([string]$Target = (Join-Path $env:USERPROFILE 'Tools\DwgBindXref'), [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\Classes\SystemFileAssociations\.dwg\shell\DwgBindXref'
if ($Uninstall) {
    if (Test-Path $key) { Remove-Item $key -Recurse -Force }
    "menu removed"
    return
}
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force $Target | Out-Null
Copy-Item (Join-Path $here 'bind-xref.ps1') -Destination $Target -Force
$script = Join-Path $Target 'bind-xref.ps1'
$icon = Get-ChildItem "$env:ProgramFiles\Autodesk\AutoCAD *\acad.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
New-Item -Path "$key\command" -Force | Out-Null
Set-ItemProperty $key -Name 'MUIVerb' -Value 'Bind Xref -> _merged.dwg'
if ($icon) { Set-ItemProperty $key -Name 'Icon' -Value "`"$($icon.FullName)`",0" }
Set-ItemProperty "$key\command" -Name '(default)' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$script`" `"%1`""
"script -> $script"
"menu   -> $key"
