# Press the Revit MCP ribbon button "MCP 服務 (開/關)" in one Revit window through UI Automation (no mouse, no
# screenshots), then close the confirmation box it shows. Use it to switch the MCP service back on in a window whose
# service was stopped by hand (a manual stop also stops it taking the port by itself).
#   mcp-toggle.ps1 -RevitPid N            start the service there (refuses when the port is already served)
#   mcp-toggle.ps1 -RevitPid N -Stop      stop the service there (same as the ribbon button when it runs)
# The button lives on the ribbon tab that holds the "MCP Tools" panel (Add-Ins); that tab is selected first.
param(
    [Parameter(Mandatory)][int]$RevitPid,
    [switch]$Stop,
    [int]$Port = $(if ($env:REVIT_MCP_PORT) { [int]$env:REVIT_MCP_PORT } else { 8964 }),
    [string]$TabName = 'Add-Ins'
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Find-PortOwner {
    $owner = $null
    foreach ($l in (netsh http show servicestate view=requestq 2>$null)) {
        if ($l -match '^\s+ID:\s*(\d+),\s*image:') { $cur = [int]$Matches[1] }
        elseif ($l -match "HTTP://[^/]*:$Port/") { $owner = $cur }
    }
    $owner
}

$owner = Find-PortOwner
if (-not $Stop -and $owner) { "Port $Port is already served by Revit PID $owner; nothing pressed."; return }
if ($Stop -and $owner -ne $RevitPid) { "Revit PID $RevitPid does not serve port $Port (owner: $owner); nothing pressed."; return }

$proc = Get-Process -Id $RevitPid -ErrorAction Stop
$win = $AE::FromHandle($proc.MainWindowHandle)
if (-not $win) { "Main window of PID $RevitPid not found."; return }

# Look things up by AutomationId with a UIA condition (filtered inside Revit). Scanning every button of the window and
# reading their names took over 90 s in a window with many ribbon tabs; this takes a few seconds.
$panelId = "${TabName}_PanelBarScrollViewer"
$buttonId = "CustomCtrl_%CustomCtrl_%$TabName%MCP Tools%MCPToggle"
function Find-Button {
    $panel = $win.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $panelId)))
    if (-not $panel) { return $null }
    $panel.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $buttonId)))
}

$btn = Find-Button
if (-not $btn) {
    # Tab not shown yet: its header is a Button child of the main window with AutomationId = tab name.
    $tab = $win.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $TabName)))
    if (-not $tab) { "Ribbon tab '$TabName' not found in PID $RevitPid."; return }
    $p = $null
    if ($tab.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke() }
    elseif ($tab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select() }
    Start-Sleep -Milliseconds 800
    $btn = Find-Button
}
if (-not $btn) { "Button 'MCP 服務 (開/關)' not found on tab '$TabName' of PID $RevitPid."; return }

# Invoke can block while the command's modal box is open, so press it from a background job.
$hwnd = [int64]$proc.MainWindowHandle
$job = Start-Job -ArgumentList $hwnd, $panelId, $buttonId -ScriptBlock {
    param($h, $panelId, $buttonId)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $AE = [System.Windows.Automation.AutomationElement]
    $TS = [System.Windows.Automation.TreeScope]
    $w = $AE::FromHandle([IntPtr]$h)
    $panel = $w.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $panelId)))
    $b = $panel.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $buttonId)))
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Close the "MCP 服務" box (TaskDialog) when it appears.
# It is a TaskDialog (#32770 child of the Revit main window, title "Revit MCP Plugin - MCP 服務") whose button UIA
# cannot see; WM_CLOSE closes it. Left open, it would block every MCP command in that window.
Add-Type -Namespace RevitPort -Name User32 -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);' -ErrorAction SilentlyContinue
$dialogCond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, '#32770')
$closed = $false
for ($i = 0; $i -lt 30 -and -not $closed; $i++) {
    Start-Sleep -Milliseconds 500
    $box = $win.FindAll($TS::Children, $dialogCond) | Where-Object { $_.Current.Name -match 'MCP' } | Select-Object -First 1
    if ($box) {
        [RevitPort.User32]::PostMessage([IntPtr]$box.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $closed = $true
    }
}
Wait-Job $job -Timeout 10 | Out-Null
Remove-Job $job -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
$now = Find-PortOwner
"Pressed in PID $RevitPid; confirmation box closed: $closed; port $Port now served by: " + $(if ($now) { "Revit PID $now" } else { 'nobody' })
