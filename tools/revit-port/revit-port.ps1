# Which Revit window serves Claude (claude using revit MCP port), what each window has open, and moving the port.
#   revit-port.ps1 [status]            Revit windows running claude using revit: role (holder = serves MCP, standby = waits),
#                                      the documents open in each, the client connected to the holder.
#                                      Read this before opening any model.
#   revit-port.ps1 take -RevitPid N    window N takes the port (within ~3 s; the old holder waits)
#   revit-port.ps1 release             the holder lets go; another waiting window takes it, else it is free ~30 s
#   -Json                              status as JSON
# Facts this relies on (addin/src/ClaudeUsingRevit/Mcp/McpServer.cs):
#   - One port (config.json "port", default 8970) on 127.0.0.1; the first Revit window holds it, the others wait and
#     take it within ~3 s when it is free. Several MCP clients may use the holder at once (plain HTTP, no lock).
#   - %APPDATA%\claude-using-revit\instances\<pid>.json: role of each window (updated every few seconds);
#     <pid>.take: ask that window to take the port; %APPDATA%\claude-using-revit\open-docs\<pid>.json: its documents.
param(
    [Parameter(Position = 0)][ValidateSet('status', 'take', 'release')][string]$Action = 'status',
    [int]$RevitPid = 0,
    [switch]$Json
)
$data = Join-Path $env:APPDATA 'claude-using-revit'
$instancesDir = Join-Path $data 'instances'
$openDocsDir = Join-Path $data 'open-docs'
$port = 8970
$cfgFile = Join-Path $data 'config.json'
if (Test-Path $cfgFile) { try { $p = (Get-Content $cfgFile -Raw | ConvertFrom-Json).port; if ($p) { $port = [int]$p } } catch { } }

$live = @(Get-Process Revit -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
foreach ($dir in $instancesDir, $openDocsDir) {
    if (Test-Path $dir) {
        Get-ChildItem $dir -Filter *.json | Where-Object { $live -notcontains [int]$_.BaseName } | Remove-Item -ErrorAction SilentlyContinue
    }
}

function Read-Json($file) { if (Test-Path $file) { try { Get-Content $file -Raw | ConvertFrom-Json } catch { $null } } }

function Get-Status {
    $holder = $null
    try { $holder = Invoke-RestMethod -Uri "http://127.0.0.1:$port/cur/status" -TimeoutSec 3 } catch { }
    $revits = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object {
        $inst = Read-Json (Join-Path $instancesDir "$($_.Id).json")
        $snap = Read-Json (Join-Path $openDocsDir "$($_.Id).json")
        [pscustomobject]@{
            Pid     = $_.Id
            Role    = if ($inst) { $inst.role } else { 'no claude using revit' }
            Note    = if ($inst) { $inst.note } else { '' }
            Started = $_.StartTime
            MB      = [int]($_.WorkingSet64 / 1MB)
            Docs    = if ($snap) { @($snap.docs | ForEach-Object { $_.title + $(if ($_.modified) { ' (modified)' } else { '' }) }) -join ', ' } else { 'UNKNOWN (no open-docs file)' }
            DocsAt  = if ($snap) { $snap.updatedUtc } else { $null }
        }
    })
    [pscustomobject]@{
        Port      = $port
        HolderPid = if ($holder) { $holder.pid } else { $null }
        Connected = if ($holder) { $holder.connected } else { $false }
        Client    = if ($holder) { $holder.lastClient } else { $null }
        Running   = if ($holder -and $holder.running) { $holder.running.tool } else { $null }
        Revit     = $revits
    }
}

if ($Action -eq 'status') {
    $s = Get-Status
    if ($Json) { $s | ConvertTo-Json -Depth 4; return }
    "MCP http://127.0.0.1:$port/mcp : " + $(if ($s.HolderPid) { "Revit PID $($s.HolderPid)" + $(if ($s.Connected) { ", connected ($($s.Client))" } else { ', no client now' }) + $(if ($s.Running) { ", running $($s.Running)" } else { '' }) } else { 'no Revit window serves it' })
    ''
    'Revit windows (Docs = every non-linked document open in that window; never open one of them again elsewhere):'
    $s.Revit | Format-Table Pid, Role, Started, MB, Docs, DocsAt -AutoSize -Wrap | Out-String -Width 220
    return
}

if ($Action -eq 'take') {
    if (-not $RevitPid) { 'Pass -RevitPid N (status lists the windows).'; return }
    if ($live -notcontains $RevitPid) { "No Revit window with PID $RevitPid."; return }
    New-Item -ItemType Directory -Force $instancesDir | Out-Null
    Set-Content (Join-Path $instancesDir "$RevitPid.take") '' -Encoding ascii
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $inst = Read-Json (Join-Path $instancesDir "$RevitPid.json")
        if ($inst -and $inst.role -eq 'holder') { "PID $RevitPid now serves http://127.0.0.1:$port/mcp"; return }
    }
    "PID $RevitPid did not take the port yet (is claude using revit running there? see its panel Claude)."
    return
}

# release
$key = Get-Content (Join-Path $data 'control.key') -ErrorAction SilentlyContinue
if (-not $key) { 'No control.key: no claude using revit window has started yet.'; return }
try {
    Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$port/cur/release" -Headers @{ 'X-CUR-Key' = $key; 'X-CUR-Pid' = 'revit-port' } -Body '{}' -ContentType 'application/json' -TimeoutSec 5 | Out-Null
    "The holder let go of port ${port}: a waiting Revit window takes it within a few seconds (else it is free ~30 s)."
}
catch { "No window holds port $port (or it did not answer): $($_.Exception.Message)" }
