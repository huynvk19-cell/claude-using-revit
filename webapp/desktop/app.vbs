' claude using revit - starts app.ps1 with no console window. A shortcut straight to powershell.exe opens
' a console, and on Windows 11 that console is a Windows Terminal window the app then lives in (closing it
' closes the app, and from Start it often never shows). wscript is a windowed host: Run ..., 0 starts
' powershell hidden, and app.ps1 shows its own window (Shown: ShowWindow restore).
Set fso = CreateObject("Scripting.FileSystemObject")
here = fso.GetParentFolderName(WScript.ScriptFullName)
ps = CreateObject("WScript.Shell").ExpandEnvironmentStrings("%WINDIR%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
CreateObject("WScript.Shell").Run """" & ps & """ -STA -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & here & "\app.ps1""", 0, False
