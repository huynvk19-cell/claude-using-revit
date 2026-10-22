# Bind every external reference of a DWG into one self-contained DWG, with AutoCAD's accoreconsole (no AutoCAD window).
#   bind-xref.ps1 <file.dwg>[,<file2.dwg> ...] [-Suffix _merged] [-BindType Insert|Bind] [-Format 2018] [-NoPause]
# The original DWG and its xref files are never changed: the result is written next to the original as <name><Suffix>.dwg.
# BindType Insert (default) keeps layer names clean (no "xref$0$" prefix); Bind keeps the prefix.
# A log of each run is kept in %LOCALAPPDATA%\DwgBindXref\logs.
param(
    [Parameter(Mandatory, Position = 0)][string[]]$Path,
    [string]$Suffix = '_merged',
    [ValidateSet('Insert', 'Bind')][string]$BindType = 'Insert',
    [string]$Format = '2018',
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'

function Find-AcCore {
    if ($env:ACCORECONSOLE -and (Test-Path $env:ACCORECONSOLE)) { return $env:ACCORECONSOLE }
    $found = Get-ChildItem "$env:ProgramFiles\Autodesk\AutoCAD *\accoreconsole.exe" -ErrorAction SilentlyContinue |
        Sort-Object { $_.Directory.Name } -Descending | Select-Object -First 1
    if (-not $found) { throw 'Khong tim thay AutoCAD accoreconsole.exe (dat bien ACCORECONSOLE neu cai o cho khac).' }
    $found.FullName
}

# The script is pure ASCII: the output name is built inside AutoCAD from DWGPREFIX/DWGNAME, so non-ASCII paths are safe.
function New-BindScript([string]$scr) {
    $bt = if ($BindType -eq 'Insert') { 1 } else { 0 }
    @"
(setvar "FILEDIA" 0)
(setvar "CMDECHO" 0)
(setvar "BINDTYPE" $bt)
(defun bxm-xrefs (unresolvedOnly / b l f) (setq b (tblnext "BLOCK" T)) (while b (setq f (cdr (assoc 70 b))) (if (and (= 4 (logand f 4)) (or (not unresolvedOnly) (/= 32 (logand f 32)))) (setq l (cons (cdr (assoc 2 b)) l))) (setq b (tblnext "BLOCK"))) l)
(progn (princ (strcat "\nBXM-BEFORE: " (itoa (length (bxm-xrefs nil))))) (foreach n (bxm-xrefs nil) (princ (strcat "\nBXM-XREF: " n))) (princ))
(progn (setq bxm-i 0) (while (and (> (length (bxm-xrefs nil)) (length (bxm-xrefs T))) (< bxm-i 6)) (command "_.-XREF" "_Bind" "*") (setq bxm-i (1+ bxm-i))) (princ))
(progn (princ (strcat "\nBXM-PASSES: " (itoa bxm-i))) (princ (strcat "\nBXM-REMAINING: " (itoa (length (bxm-xrefs nil))))) (foreach n (bxm-xrefs nil) (princ (strcat "\nBXM-LEFT: " n))) (princ))
(progn (setq bxm-out (strcat (getvar "DWGPREFIX") (substr (getvar "DWGNAME") 1 (- (strlen (getvar "DWGNAME")) 4)) "$Suffix.dwg")) (command "_.SAVEAS" "$Format" bxm-out) (princ (strcat "\nBXM-SAVED: " (if (findfile bxm-out) "yes" "no"))) (princ))
"@ | Set-Content -LiteralPath $scr -Encoding ASCII
}

$acc = Find-AcCore
$logDir = Join-Path $env:LOCALAPPDATA 'DwgBindXref\logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$scr = Join-Path $env:TEMP "bind-xref-$PID.scr"
New-BindScript $scr
$results = @()
foreach ($p in $Path) {
    $src = (Resolve-Path -LiteralPath $p).Path
    if ([IO.Path]::GetExtension($src) -ne '.dwg') { $results += "BO QUA (khong phai DWG): $src"; continue }
    $out = Join-Path (Split-Path $src) ([IO.Path]::GetFileNameWithoutExtension($src) + "$Suffix.dwg")
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
    Write-Host "Dang bind: $src"
    $log = Join-Path $logDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [IO.Path]::GetFileNameWithoutExtension($src) + '.log')
    $enc = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.Encoding]::Unicode
        $text = (& $acc /i $src /s $scr /l en-US 2>&1 | Out-String)
    }
    finally { [Console]::OutputEncoding = $enc }
    $text = $text -replace "`0", ''
    Set-Content -LiteralPath $log -Value $text -Encoding UTF8
    $get = { param($k) ([regex]::Matches($text, "(?m)^BXM-${k}: ?([^\r\n]*)") | ForEach-Object { $_.Groups[1].Value.Trim() }) }
    $before = (& $get 'BEFORE') | Select-Object -First 1
    $left = (& $get 'REMAINING') | Select-Object -First 1
    $leftNames = & $get 'LEFT'
    $ok = (Test-Path -LiteralPath $out) -and ($left -eq '0')
    $line = if ($ok) { "OK  $before xref -> 0  =>  $out" }
            elseif (Test-Path -LiteralPath $out) { "CHUA HET: con $left xref ($($leftNames -join ', '))  =>  $out" }
            else { "LOI: khong luu duoc file. Xem log $log" }
    Write-Host $line
    $results += $line
}
Remove-Item -LiteralPath $scr -Force -ErrorAction SilentlyContinue
if (-not $NoPause) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show(($results -join "`n`n"), 'Bind Xref (merged DWG)')
}
