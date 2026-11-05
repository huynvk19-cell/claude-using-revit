# Build the claude using revit add-in for one or more Revit versions into addin\dist\<version>\.
#   powershell -ExecutionPolicy Bypass -File addin\build.ps1                 the Revit versions installed on this PC
#   powershell -ExecutionPolicy Bypass -File addin\build.ps1 -Revit 2024,2025 -Check
#   -Check : also compile every revit-commands\*.cs against that Revit's API (addin\tests\CommandCheck)
# Needs the .NET SDK 8 or newer (10 for Revit 2027): winget install Microsoft.DotNet.SDK.8
# dist\<version>\ = ClaudeUsingRevit.dll + IronPython + Lib\ (Python standard library) + roslyn\ (C# compiler of the commands).
param([string[]]$Revit, [switch]$Check)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found: winget install Microsoft.DotNet.SDK.8 (then open a new terminal)'
}

function Get-InstalledRevit {
    $found = @()
    foreach ($root in @("$env:ProgramFiles\Autodesk")) {
        if (Test-Path $root) {
            Get-ChildItem $root -Directory -Filter 'Revit 20*' | ForEach-Object {
                if ($_.Name -match 'Revit (20\d\d)$' -and (Test-Path (Join-Path $_.FullName 'Revit.exe'))) { $found += $Matches[1] }
            }
        }
    }
    $found | Sort-Object -Unique
}

if (-not $Revit -or $Revit.Count -eq 0) { $Revit = @(Get-InstalledRevit) }
if (-not $Revit -or $Revit.Count -eq 0) { throw 'No Revit found in Program Files\Autodesk: pass -Revit 2024,2025' }

foreach ($v in $Revit) {
    $year = [int]$v
    if ($year -lt 2023) { "Revit $v is not supported (2023 or newer)"; continue }
    $compilerTfm = if ($year -lt 2025) { 'net48' } else { 'net8.0' }
    "== Revit $v"
    dotnet build (Join-Path $here 'src\ClaudeUsingRevit') -c Release "-p:RevitVersion=$v" -nologo -v q
    if ($LASTEXITCODE) { throw "build failed for Revit $v" }
    dotnet build (Join-Path $here 'src\ClaudeUsingRevit.Compiler') -c Release -f $compilerTfm -nologo -v q
    if ($LASTEXITCODE) { throw "compiler build failed ($compilerTfm)" }

    $dist = Join-Path $here "dist\$v"
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Force $dist | Out-Null
    robocopy (Join-Path $here "src\ClaudeUsingRevit\bin\Release\$v") $dist /E /XF *.pdb compile-refs.txt /XD runtimes /NFL /NDL /NJH /NJS /NP | Out-Null
    robocopy (Join-Path $here "src\ClaudeUsingRevit.Compiler\bin\Release\$compilerTfm") (Join-Path $dist 'roslyn') *.dll /NFL /NDL /NJH /NJS /NP | Out-Null
    $global:LASTEXITCODE = 0
    "   -> $dist"

    if ($Check) {
        dotnet run --project (Join-Path $here 'tests\CommandCheck') -c Release -- $v
        if ($LASTEXITCODE) { "   some commands do not compile for Revit $v (see above)" }
    }
}
