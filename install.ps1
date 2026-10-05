# Install the drafting skills and domain files for Claude Code (user scope).
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
