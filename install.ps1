# Install the drafting skills, workflows, subagents, domain files and (optionally) the Revit MCP dynamic commands for Claude Code.
#   -CommandsDir <path>  : the Revit MCP 'dynamic-commands' folder; the commands in revit-commands\ are copied there
#                          (same-name files are overwritten). Default: $env:REVIT_MCP_COMMANDS, skipped when empty.
#   -WorkStatus          : also install the status window scripts to %USERPROFILE%\Tools\WorkStatus
#   -DwgBindXref         : also install the DWG bind-xref tool + its Explorer right-click menu (HKCU, no admin)
#   -PyRevit             : also install the pyRevit extension (ribbon tab "claude using revit") to
#                          %APPDATA%\pyRevit\Extensions; then pyRevit > Reload in Revit
#   -WebApp              : also install the web app (webapp\: every tool from a browser, iPad on the LAN) to
#                          %APPDATA%\claude-using-revit\webapp and (re)start its server; Revit side = the pyRevit extension
param([string]$CommandsDir = $env:REVIT_MCP_COMMANDS, [switch]$WorkStatus, [switch]$DwgBindXref, [switch]$PyRevit, [switch]$WebApp)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$claude = Join-Path $env:USERPROFILE '.claude'
$skills = Join-Path $claude 'skills'
$domain = Join-Path $claude 'drafting-domain'
New-Item -ItemType Directory -Force $skills, $domain | Out-Null
Get-ChildItem (Join-Path $root 'skills') -Directory | ForEach-Object {
    Copy-Item $_.FullName -Destination $skills -Recurse -Force
    "skill   -> $skills\$($_.Name)"
}
$agents = Join-Path $claude 'agents'
New-Item -ItemType Directory -Force $agents | Out-Null
Get-ChildItem (Join-Path $root 'agents\*.md') | ForEach-Object {
    Copy-Item $_.FullName -Destination $agents -Force
    "agent   -> $agents\$($_.Name)"
}
Copy-Item (Join-Path $root 'domain\*') -Destination $domain -Recurse -Force
foreach ($d in 'templates', 'tools', 'workflows') {
    Copy-Item (Join-Path $root $d) -Destination $domain -Recurse -Force
}
"domain  -> $domain"
if ($CommandsDir) {
    if (-not (Test-Path $CommandsDir)) { throw "CommandsDir not found: $CommandsDir" }
    $n = 0
    Get-ChildItem (Join-Path $root 'revit-commands\*.cs') | ForEach-Object { Copy-Item $_.FullName -Destination $CommandsDir -Force; $n++ }
    "commands -> $CommandsDir ($n files)"
}
else { "commands: skipped (pass -CommandsDir <Revit MCP dynamic-commands folder> or set REVIT_MCP_COMMANDS)" }
if ($WorkStatus) {
    $ws = Join-Path $env:USERPROFILE 'Tools\WorkStatus'
    New-Item -ItemType Directory -Force $ws | Out-Null
    Copy-Item (Join-Path $root 'tools\work-status\*.ps1') -Destination $ws -Force
    "work status -> $ws"
}
if ($DwgBindXref) { & (Join-Path $root 'tools\dwg-bind-xref\install-menu.ps1') }
if ($PyRevit) {
    $ext = Join-Path $env:APPDATA 'pyRevit\Extensions'
    New-Item -ItemType Directory -Force $ext | Out-Null
    Get-ChildItem (Join-Path $root 'pyrevit') -Directory -Filter '*.extension' | ForEach-Object {
        $dst = Join-Path $ext $_.Name
        # mirror (stale files removed); a file Revit holds open (ribbon icon) is skipped, not fatal
        robocopy $_.FullName $dst /MIR /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { "pyrevit: some files were in use and not replaced (robocopy $LASTEXITCODE)" }
        $global:LASTEXITCODE = 0
        "pyrevit -> $dst"
    }
}
if ($WebApp) {
    # web app server (webapp\server.mjs); the pyRevit startup starts it with Revit, here it is restarted on the new copy
    $wa = Join-Path $env:APPDATA 'claude-using-revit\webapp'
    Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" |
        Where-Object { $_.CommandLine -like '*claude-using-revit\webapp\server.mjs*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "web app: stopped old server (PID $($_.ProcessId))" }
    $island = @(Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" | Where-Object { $_.CommandLine -like '*claude-using-revit\webapp\desktop\island.ps1*' })
    $island | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "island: stopped (PID $($_.ProcessId)), restarted below" }
    if ($island) { Start-Sleep -Milliseconds 800 }     # its WebView2 dlls are released
    if (Test-Path $wa) { Remove-Item $wa -Recurse -Force }
    Copy-Item (Join-Path $root 'webapp') -Destination $wa -Recurse -Force
    Get-ChildItem (Join-Path $root 'revit-commands\*.cs') | ForEach-Object { $_.BaseName } |
        Set-Content (Join-Path $wa 'revit-commands.txt') -Encoding ascii
    # desktop island (webapp\desktop\island.ps1): the WebView2 SDK dlls come from a local install, not this repo
    $sdk = Get-ChildItem 'C:\Program Files\Autodesk', 'C:\Program Files (x86)\Microsoft', "$env:LOCALAPPDATA\Programs" -Recurse -Depth 6 `
        -Filter 'Microsoft.Web.WebView2.WinForms.dll' -ErrorAction SilentlyContinue | Sort-Object { [version]$_.VersionInfo.FileVersion } -Descending |
        Where-Object { Test-Path (Join-Path $_.DirectoryName 'Microsoft.Web.WebView2.Core.dll') } | Select-Object -First 1
    # the native loader must be x64 (PE machine 0x8664): runtimes\win-x64\native first
    $loader = if ($sdk) { @((Join-Path $sdk.DirectoryName 'runtimes\win-x64\native\WebView2Loader.dll'), (Join-Path $sdk.DirectoryName 'WebView2Loader.dll')) |
        Where-Object { (Test-Path $_) -and ($b = [IO.File]::ReadAllBytes($_)) -and [BitConverter]::ToUInt16($b, [BitConverter]::ToInt32($b, 0x3c) + 4) -eq 0x8664 } |
        Select-Object -First 1 }
    if ($sdk -and $loader) {
        $dl = Join-Path $wa 'desktop\lib'
        New-Item -ItemType Directory -Force $dl | Out-Null
        foreach ($f in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll') { Copy-Item (Join-Path $sdk.DirectoryName $f) $dl -Force }
        Copy-Item $loader $dl -Force
        $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Revit Island.lnk'
        $sh = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
        $sh.TargetPath = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
        $sh.Arguments = "-STA -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$wa\desktop\island.ps1`""
        $sh.WorkingDirectory = "$wa\desktop"
        $sh.Save()
        "island -> $lnk (WebView2 SDK from $($sdk.DirectoryName))"
    }
    else { "island: WebView2 SDK dlls not found on this PC; the desktop island is not installed" }
    $node = (Get-Command node -ErrorAction SilentlyContinue).Source
    if ($node) {
        Start-Process $node -ArgumentList "`"$wa\server.mjs`"" -WorkingDirectory $wa -WindowStyle Hidden
        "web app -> $wa (started; address + access code: $env:APPDATA\claude-using-revit\webapp.json)"
    }
    else { "web app -> $wa (node not found; Revit starts it once node is installed)" }
    if ($island) { Start-Process "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList "-STA -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$wa\desktop\island.ps1`"" -WindowStyle Hidden }
}
