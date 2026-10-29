# Who holds the Revit MCP port, and release it.
#   revit-port.ps1 [status]                 Revit windows, which one listens on the port, which node bridge is connected
#   revit-port.ps1 release [-BridgePid N]   stop the node bridge connected to the port (default: the connected one)
#   revit-port.ps1 release -AllBridges      stop every Revit MCP node bridge (connected or idle)
#   -Port N                                  default 8964 (or $env:REVIT_MCP_PORT)
#   -Json                                    status as JSON
# Facts this relies on:
#   - The Revit add-in listens through HTTP.sys, so Get-NetTCPConnection shows PID 4 as the listener; the Revit
#     window that really owns it comes from 'netsh http show servicestate'.
#   - Only one MCP client at a time (others get HTTP 409). A node bridge connects on its first tool call and then
#     reconnects every 5 s whenever the connection drops, so the ribbon "Switch/Release" button alone does not free
#     the port for long: the bridge itself must stop (ending its Claude session normally stops it, but a Remote
#     Control session that is stopped can leave it running).
#   - Revit windows started while another one holds the port wait and take it ~10 s after it is freed (stop_mcp_service
#     or closing that window). With several waiting windows, whichever polls first wins.
param(
    [Parameter(Position = 0)][ValidateSet('status', 'release')][string]$Action = 'status',
    [int]$Port = $(if ($env:REVIT_MCP_PORT) { [int]$env:REVIT_MCP_PORT } else { 8964 }),
    [int]$BridgePid = 0,
    [switch]$AllBridges,
    [switch]$Json
)

$bridgePattern = 'MCP-Server[\\/]build[\\/]index\.js'

function Get-PortOwnerRevit([int]$port) {
    $lines = netsh http show servicestate view=requestq 2>$null
    $owner = $null
    foreach ($l in $lines) {
        if ($l -match '^\s+ID:\s*(\d+),\s*image:\s*(.+)$') { $cur = [int]$Matches[1] }
        elseif ($l -match "HTTP://[^/]*:$port/") { $owner = $cur }
    }
    $owner
}

function Get-Bridges {
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
        Where-Object { $_.CommandLine -match $bridgePattern } |
        ForEach-Object {
            $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($_.ParentProcessId)" -ErrorAction SilentlyContinue
            [pscustomobject]@{
                Pid        = $_.ProcessId
                Started    = $_.CreationDate
                ParentPid  = $_.ParentProcessId
                ParentName = $parent.Name
                ParentStarted = $parent.CreationDate
                Connected  = $false
            }
        }
}

function Get-Status {
    $connected = @(Get-NetTCPConnection -RemotePort $Port -State Established -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    $bridges = @(Get-Bridges)
    foreach ($b in $bridges) { $b.Connected = $connected -contains $b.Pid }
    $ownerPid = Get-PortOwnerRevit $Port
    $revits = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{
            Pid       = $_.Id
            Started   = $_.StartTime
            HoldsPort = ($_.Id -eq $ownerPid)
            MB        = [int]($_.WorkingSet64 / 1MB)
            Window    = $_.MainWindowTitle
        }
    })
    [pscustomobject]@{
        Port          = $Port
        PortOwnerRevit = $ownerPid
        Revit         = $revits
        Bridges       = $bridges
        OtherClients  = @($connected | Where-Object { $bridges.Pid -notcontains $_ })
    }
}

if ($Action -eq 'status') {
    $s = Get-Status
    if ($Json) { $s | ConvertTo-Json -Depth 4; return }
    "Port $Port listener: " + $(if ($s.PortOwnerRevit) { "Revit PID $($s.PortOwnerRevit)" } else { 'none (no Revit serves MCP)' })
    ''
    'Revit windows:'
    $s.Revit | Format-Table Pid, HoldsPort, Started, MB, Window -AutoSize -Wrap | Out-String -Width 220
    'Node MCP bridges (Connected = holds the lock):'
    if ($s.Bridges.Count) { $s.Bridges | Format-Table Pid, Connected, Started, ParentPid, ParentName, ParentStarted -AutoSize | Out-String -Width 220 }
    else { '  none' }
    if ($s.OtherClients.Count) { "Other processes connected to the port: $($s.OtherClients -join ', ')" }
    return
}

# release
$s = Get-Status
$targets = if ($AllBridges) { $s.Bridges }
           elseif ($BridgePid) { $s.Bridges | Where-Object Pid -eq $BridgePid }
           else { $s.Bridges | Where-Object Connected }
if (-not $targets) {
    if ($BridgePid) { "PID $BridgePid is not a Revit MCP node bridge; nothing stopped." }
    else { 'No bridge holds the port; nothing to release.' }
    return
}
foreach ($t in $targets) {
    try {
        Stop-Process -Id $t.Pid -Force -ErrorAction Stop
        "Stopped bridge PID $($t.Pid) (parent $($t.ParentName) $($t.ParentPid), connected=$($t.Connected))"
    } catch {
        "Could not stop PID $($t.Pid): $($_.Exception.Message). Fallback: Revit ribbon 'MCP Tools > Switch/Release', then end that Claude session."
    }
}
Start-Sleep -Seconds 2
$after = @(Get-NetTCPConnection -RemotePort $Port -State Established -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
if ($after.Count) { "Port still connected by PID $($after -join ', ')" } else { "Port $Port free: the next MCP tool call of any session takes it." }
