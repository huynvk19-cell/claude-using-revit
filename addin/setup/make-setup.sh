#!/usr/bin/env bash
# Build ClaudeUsingRevit-Setup.exe: the add-in for Revit 2023-2027 + the ribbon tools + the commands + Claude's skills,
# all in one exe for people who install by double-clicking. Needs the .NET SDK 8 and 10 (Linux, macOS or Git Bash).
#   bash addin/setup/make-setup.sh            -> addin/dist/ClaudeUsingRevit-Setup.exe
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
addin="$root/addin"
out="$addin/dist"
pay="$out/payload"
version="${SETUP_VERSION:-$(date +%Y.%m.%d)}"
rm -rf "$pay" "$out/payload.zip"
mkdir -p "$pay"

for v in 2023 2024 2025 2026 2027; do
    dotnet build "$addin/src/ClaudeUsingRevit" -c Release -p:RevitVersion=$v -nologo -v q | grep -E " error |rror\(s\)" || true
    tfm=net8.0; [ $v -lt 2025 ] && tfm=net48
    bin="$addin/src/ClaudeUsingRevit/bin/Release/$v"
    [ -f "$bin/ClaudeUsingRevit.dll" ] || { echo "build failed for Revit $v"; exit 1; }
    mkdir -p "$pay/addin/$v/roslyn"
    (cd "$bin" && tar cf - --exclude='*.pdb' --exclude=compile-refs.txt --exclude=runtimes --exclude=Lib .) | (cd "$pay/addin/$v" && tar xf -)
    echo "Revit $v: $(ls "$pay/addin/$v" | wc -l) files"
done
dotnet build "$addin/src/ClaudeUsingRevit.Compiler" -c Release -nologo -v q | grep -E " error |rror\(s\)" || true
for v in 2023 2024 2025 2026 2027; do
    tfm=net8.0; [ $v -lt 2025 ] && tfm=net48
    cp "$addin/src/ClaudeUsingRevit.Compiler/bin/Release/$tfm/"*.dll "$pay/addin/$v/roslyn/"
done
# one copy of the Python standard library (the installer puts it next to each add-in)
mkdir -p "$pay/python"
cp -r "$addin/src/ClaudeUsingRevit/bin/Release/2025/Lib" "$pay/python/Lib"

cp -r "$addin/extension" "$pay/extension"
mkdir -p "$pay/commands" && cp "$root"/revit-commands/*.cs "$pay/commands/"
# what install.ps1 copies to ~/.claude
mkdir -p "$pay/claude/skills" "$pay/claude/agents" "$pay/claude/drafting-domain"
cp -r "$root"/skills/* "$pay/claude/skills/"
cp "$root"/agents/*.md "$pay/claude/agents/"
cp -r "$root"/domain/* "$pay/claude/drafting-domain/"
for d in templates tools workflows; do cp -r "$root/$d" "$pay/claude/drafting-domain/$d"; done
echo "$version" > "$pay/version.txt"

(cd "$pay" && find . -name '__pycache__' -prune -o -name '*.pyc' -prune -o -type f -print | sed 's#^\./##' | sort | zip -q -9 -X ../payload.zip -@)
echo "payload: $(du -h "$out/payload.zip" | cut -f1)"

dotnet build "$addin/setup" -c Release -nologo -v q -p:Version="$version" -p:PayloadZip="$out/payload.zip" | grep -E " error |rror\(s\)" || true
cp "$addin/setup/bin/Release/net48/ClaudeUsingRevit-Setup.exe" "$out/ClaudeUsingRevit-Setup.exe"
echo "-> $out/ClaudeUsingRevit-Setup.exe ($(du -h "$out/ClaudeUsingRevit-Setup.exe" | cut -f1), version $version)"
