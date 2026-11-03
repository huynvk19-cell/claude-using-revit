# Move %APPDATA%\claude-using-revit out of the Claude desktop app's private store, then reinstall the
# web app where Windows (Start Menu, Revit, Explorer) can see it.
#
# Why: the Claude desktop app is an MSIX package. Folders its sessions create under %APPDATA% go to
# %LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming instead, visible only to processes of that
# package. An install.ps1 -WebApp run by Claude therefore left the real folder without the web app:
# the Start Menu shortcut found no app.vbs, and Revit's startup found no server.mjs.
#
# Run it YOURSELF from a normal terminal (Start > Terminal), not from a Claude session:
#   powershell -ExecutionPolicy Bypass -File <repo>\tools\appdata-out-of-claude.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# a process started by the Claude app is inside its package (and sees the private store): refuse
$p = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
for ($i = 0; $p -and $i -lt 12; $i++) {
    if ($p.Name -match '^claude') { throw 'Started from the Claude app: open Terminal from the Start menu and run this there.' }
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.ParentProcessId)"
}

# close the app and the web server first (their files are copied)
Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*claude-using-revit\webapp\desktop\app.ps1*') -or
    ($_.Name -eq 'node.exe' -and $_.CommandLine -like '*claude-using-revit\webapp\server.mjs*') } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "stopped $($_.Name) $($_.ProcessId)" }
Start-Sleep 1

$real = Join-Path $env:APPDATA 'claude-using-revit'
foreach ($pkg in Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Directory -Filter 'Claude_*') {
    $virt = Join-Path $pkg.FullName 'LocalCache\Roaming\claude-using-revit'
    if (-not (Test-Path $virt)) { continue }
    # newer files only: what Revit wrote to the real folder (web-bridge, pdf_export.json) stays
    robocopy $virt $real /E /XO /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copy failed (robocopy $LASTEXITCODE): $virt" }
    $bak = "$virt.moved-$(Get-Date -Format yyyyMMdd-HHmm)"
    Rename-Item $virt $bak                    # kept as a backup; the package now sees the real folder
    "moved $virt -> $real (backup: $bak)"
}

& (Join-Path $root 'install.ps1') -WebApp
Start-Process "$env:WINDIR\System32\wscript.exe" -ArgumentList "`"$real\webapp\desktop\app.vbs`""
'done: the app opens from Start; Claude sessions now write to the real folder too'
