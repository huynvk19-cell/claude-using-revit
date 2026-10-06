# Controller for the standalone status window.
#   ws.ps1 show   -Title '...' -Message '...' [-Step '1/3']
#   ws.ps1 update -Message '...' [-Step '2/3'] [-Title '...']
#   ws.ps1 close
param(
  [Parameter(Mandatory = $true, Position = 0)][ValidateSet('show', 'update', 'close')][string]$Action,
  [string]$Title, [string]$Message, [string]$Step
)
$dir = $PSScriptRoot
$file = Join-Path $dir 'status.json'
$pidFile = Join-Path $dir 'host.pid'

function Running {
  if (-not (Test-Path $pidFile)) { return $false }
  $p = Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
  return ($null -ne $p -and $p.ProcessName -like 'powershell*')
}
function Save($o) { [IO.File]::WriteAllText($file, ($o | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding $false)) }

$cur = $null
if ((Test-Path $file) -and $Action -ne 'show') { try { $cur = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } }
if ($Action -eq 'close') {
  if (Test-Path $file) { Save @{ state = 'close' } }
  'closed'; return
}
$o = [ordered]@{
  title   = if ($Title) { $Title } elseif ($cur) { $cur.title } else { 'Đang xử lý' }
  message = if ($Message) { $Message } elseif ($cur) { $cur.message } else { '' }
  step    = if ($PSBoundParameters.ContainsKey('Step')) { $Step } elseif ($cur -and $Action -eq 'update') { $cur.step } else { '' }
  state   = 'open'
}
Save $o
if (-not (Running)) {
  Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$dir\host.ps1`"") -WindowStyle Hidden
  'started'
} else { 'updated' }
