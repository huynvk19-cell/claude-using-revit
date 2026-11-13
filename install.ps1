# Install the drafting skills, workflows, subagents and domain files for Claude Code, and (optionally) the claude using revit
# Revit add-in: the ribbon tab "claude using revit" + the MCP server Claude uses. Needs neither pyRevit nor another Revit MCP.
#   -AddIn          : build (addin\build.ps1, needs the .NET SDK) and install claude using revit for the Revit versions on this PC:
#                     %APPDATA%\claude-using-revit\bin\<version>\ (the add-in), \extension\ (the ribbon tools, Python),
#                     \commands\ (revit-commands\*.cs = the MCP tools), %APPDATA%\Autodesk\Revit\Addins\<version>\ClaudeUsingRevit.addin.
#                     Revit may stay open: a locked DLL is renamed and replaced, open windows load it at their next start.
#                     Removes the old pyRevit extension ClaudeUsingRevit.extension.
#   -Revit 2024,2025: the Revit versions for -AddIn (default: the ones installed)
#   -Dist <folder>  : with -AddIn, install a ready build (<folder>\<version>\ClaudeUsingRevit.dll) instead of building.
#                     Most people need none of this file: ClaudeUsingRevit-Setup.exe (release setup-latest) does it all.
#   -Dev            : with -AddIn, claude using revit reads extension\ and commands\ straight from this repo checkout
#                     (config.json extensionDir / commandsDir): an edit applies on the next click / tool call.
#   -RegisterClaude : add the MCP server to Claude Code (claude mcp add --transport http --scope user claude-using-revit ...)
#   -DwgBindXref    : also install the DWG bind-xref tool + its Explorer right-click menu (HKCU, no admin)
param([switch]$AddIn, [string[]]$Revit, [string]$Dist, [switch]$Dev, [switch]$RegisterClaude, [switch]$DwgBindXref)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$claude = Join-Path $env:USERPROFILE '.claude'
$skills = Join-Path $claude 'skills'
$domain = Join-Path $claude 'drafting-domain'
$data = Join-Path $env:APPDATA 'claude-using-revit'
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

function Mirror($from, $to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    robocopy $from $to /MIR /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($code -ge 8) { "   some files were in use and not replaced (robocopy $code): close Revit and run again" }
}

# The add-in's own folder while Revit may run: Revit locks the DLLs it loaded, but Windows lets a loaded DLL be renamed.
# A changed file that is locked is renamed to <name>.old-<time> and the new one takes its place: open Revit windows keep
# the old code, the next start loads the new. Leftover .old-* files go on a later run, once nothing holds them.
function PlaceAddIn($from, $to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    Get-ChildItem $to -Recurse -File -Filter '*.old-*' | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    $moved = 0
    foreach ($file in Get-ChildItem $from -Recurse -File) {
        $src = $file.FullName       # not $_: inside catch it is the error
        $target = Join-Path $to $src.Substring($from.Length).TrimStart('\')
        if ((Test-Path $target) -and (Get-FileHash $target).Hash -eq (Get-FileHash $src).Hash) { continue }
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        try { Copy-Item $src $target -Force -ErrorAction Stop }
        catch {
            $old = $target + ".old-$stamp"
            Rename-Item $target (Split-Path $old -Leaf) -ErrorAction Stop
            try { Copy-Item $src $target -Force -ErrorAction Stop }
            catch { Rename-Item $old (Split-Path $target -Leaf); throw }     # never leave the add-in without its file
            $moved++
        }
    }
    if ($moved) { "   $moved file(s) in use by Revit were renamed and replaced: open Revit windows load the new add-in at their next start" }
}

if ($AddIn) {
    if ($Dist) { if (-not (Test-Path (Join-Path $Dist '*\ClaudeUsingRevit.dll'))) { throw "no <version>\ClaudeUsingRevit.dll in $Dist" } }
    else { & (Join-Path $root 'addin\build.ps1') -Revit $Revit; $Dist = Join-Path $root 'addin\dist' }
    $built = Get-ChildItem $Dist -Directory | Where-Object { $_.Name -match '^20\d\d$' }
    if (-not $Revit) {
        $have = Get-ChildItem "$env:ProgramFiles\Autodesk" -Directory -Filter 'Revit 20*' -ErrorAction SilentlyContinue |
            ForEach-Object { if ($_.Name -match '(20\d\d)$') { $Matches[1] } }
        if ($have) { $built = $built | Where-Object { $have -contains $_.Name } }
    }
    if ($Revit) { $built = $built | Where-Object { $Revit -contains $_.Name } }
    foreach ($b in $built) {
        $v = $b.Name
        $bin = Join-Path $data "bin\$v"
        PlaceAddIn $b.FullName $bin
        $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
        New-Item -ItemType Directory -Force $addins | Out-Null
        $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>claude using revit</Name>
    <Assembly>$bin\ClaudeUsingRevit.dll</Assembly>
    <AddInId>8D3C5F2E-6B1A-4C9E-9A57-2F0D6C1E4B73</AddInId>
    <FullClassName>ClaudeUsingRevit.App</FullClassName>
    <VendorId>CLAUDEUSINGREVIT</VendorId>
    <VendorDescription>claude using revit: ribbon tools + MCP server for Claude</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
        Set-Content (Join-Path $addins 'ClaudeUsingRevit.addin') $manifest -Encoding UTF8
        "add-in  -> $bin (Revit $v, manifest $addins\ClaudeUsingRevit.addin)"
    }
    if (-not $Dev) {
        Mirror (Join-Path $root 'addin\extension') (Join-Path $data 'extension')
        "ribbon  -> $data\extension"
        Mirror (Join-Path $root 'revit-commands') (Join-Path $data 'commands')
        "commands -> $data\commands ($((Get-ChildItem (Join-Path $data 'commands') -Filter *.cs).Count) files)"
    }
    # config.json: the repo checkout (-Dev) or the installed copies
    $cfgFile = Join-Path $data 'config.json'
    $cfg = if (Test-Path $cfgFile) { Get-Content $cfgFile -Raw | ConvertFrom-Json } else { [pscustomobject]@{ port = 8970; autoStart = $true } }
    foreach ($k in 'extensionDir', 'commandsDir') { if (-not ($cfg.PSObject.Properties.Name -contains $k)) { $cfg | Add-Member $k $null } }
    if ($Dev) {
        $cfg.extensionDir = Join-Path $root 'addin\extension'
        $cfg.commandsDir = Join-Path $root 'revit-commands'
        "dev     -> claude using revit reads $($cfg.extensionDir) and $($cfg.commandsDir)"
    }
    else { $cfg.extensionDir = $null; $cfg.commandsDir = $null }
    $cfg | ConvertTo-Json | Set-Content $cfgFile -Encoding UTF8
    # the pyRevit extension this add-in replaces (two ribbon tabs otherwise)
    $old = Join-Path $env:APPDATA 'pyRevit\Extensions\ClaudeUsingRevit.extension'
    if (Test-Path $old) { Remove-Item $old -Recurse -Force; "removed old pyRevit extension $old" }
    $port = if ($cfg.port) { $cfg.port } else { 8970 }
    "MCP     -> http://127.0.0.1:$port/mcp (starts with Revit; ribbon tab claude using revit, panel Claude shows the connection)"
}

if ($RegisterClaude) {
    $port = 8970
    $cfgFile = Join-Path $data 'config.json'
    if (Test-Path $cfgFile) { $p = (Get-Content $cfgFile -Raw | ConvertFrom-Json).port; if ($p) { $port = $p } }
    $cli = Get-Command claude -ErrorAction SilentlyContinue
    if ($cli) {
        & claude mcp remove --scope user claude-using-revit 2>$null | Out-Null
        & claude mcp add --transport http --scope user claude-using-revit "http://127.0.0.1:$port/mcp"
        "claude  -> MCP server 'claude-using-revit' added (user scope). An old 'revit-mcp' entry can go: claude mcp remove revit-mcp"
    }
    else { "claude CLI not found: claude mcp add --transport http --scope user claude-using-revit http://127.0.0.1:$port/mcp" }
}

# the status window and the web app of older versions are gone: their scripts, copies, state files, its MCP command
Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*claude-using-revit\webapp\server.mjs*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "web app: stopped old server (PID $($_.ProcessId))" }
foreach ($old in (Join-Path $env:USERPROFILE 'Tools\WorkStatus'), (Join-Path $domain 'tools\work-status'), (Join-Path $data 'commands\work_status.cs'),
                 (Join-Path $data 'webapp'), (Join-Path $data 'webapp.json'), (Join-Path $data 'web-bridge')) {
    if (Test-Path $old) { Remove-Item $old -Recurse -Force; "removed $old" }
}
if ($DwgBindXref) { & (Join-Path $root 'tools\dwg-bind-xref\install-menu.ps1') }
