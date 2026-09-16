$ErrorActionPreference = "Stop"

$payloadRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installDir = Join-Path $env:LOCALAPPDATA "Programs\CodexQuotaMonitor"
$exePath = Join-Path $installDir "CodexQuotaMonitor.exe"
$uninstallPath = Join-Path $installDir "uninstall.ps1"

Get-Process -Name "CodexQuotaMonitor" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

foreach ($fileName in @("CodexQuotaMonitor.exe", "codex-feishu-hook.ps1", "README.md", "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $payloadRoot $fileName) -Destination (Join-Path $installDir $fileName) -Force
}
Copy-Item -LiteralPath (Join-Path $payloadRoot "uninstall.ps1") -Destination $uninstallPath -Force

$shell = New-Object -ComObject WScript.Shell
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Save()
}

New-Shortcut (Join-Path $startMenuDir "Codex Quota Monitor.lnk") $exePath
$desktopPath = [Environment]::GetFolderPath("Desktop")
if (-not [string]::IsNullOrWhiteSpace($desktopPath)) {
    New-Shortcut (Join-Path $desktopPath "Codex Quota Monitor.lnk") $exePath
}

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor"
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "Codex 账号额度状态栏监控工具" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "0.3.1" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "sorry-every" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $installDir -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $exePath -PropertyType String -Force | Out-Null
$uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPath`""
New-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value $uninstallCommand -PropertyType String -Force | Out-Null

Start-Process -FilePath $exePath
