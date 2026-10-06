# Install the drafting skills, domain files and (optionally) the Revit MCP dynamic commands for Claude Code.
#   -CommandsDir <path>  : the Revit MCP 'dynamic-commands' folder; the commands in revit-commands\ are copied there
#                          (same-name files are overwritten). Default: $env:REVIT_MCP_COMMANDS, skipped when empty.
#   -WorkStatus          : also install the status window scripts to %USERPROFILE%\Tools\WorkStatus
param([string]$CommandsDir = $env:REVIT_MCP_COMMANDS, [switch]$WorkStatus)
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
Copy-Item (Join-Path $root 'domain\*') -Destination $domain -Recurse -Force
foreach ($d in 'templates', 'tools') {
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
