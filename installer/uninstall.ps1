$ErrorActionPreference = "SilentlyContinue"

$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex Quota Monitor.lnk"
$desktopPath = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktopPath "Codex Quota Monitor.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$settingsDirectory = Join-Path $env:APPDATA "CodexQuotaMonitor"

Get-Process -Name "CodexQuotaMonitor" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuShortcut -Force
Remove-Item -LiteralPath $desktopShortcut -Force
Remove-Item -Path $uninstallKey -Recurse -Force
Remove-ItemProperty -Path $runKey -Name "CodexQuotaMonitor" -Force
Remove-Item -LiteralPath (Join-Path $settingsDirectory "settings.json") -Force
Remove-Item -LiteralPath (Join-Path $settingsDirectory "codex-feishu-hook.ps1") -Force
Start-Sleep -Milliseconds 300
Remove-Item -LiteralPath $installDir -Recurse -Force
