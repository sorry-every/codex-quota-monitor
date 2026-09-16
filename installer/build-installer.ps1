$ErrorActionPreference = "Stop"

$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $installerRoot
$releaseDir = Join-Path $projectRoot "release"
$stagingDir = Join-Path $projectRoot ".installer-staging"
$sedPath = Join-Path $installerRoot "CodexQuotaMonitor.sed"
$outputPath = Join-Path $releaseDir "CodexQuotaMonitor-0.3.0-Setup.exe"
$iexpressPath = Join-Path $env:WINDIR "System32\iexpress.exe"

if (-not (Test-Path -LiteralPath $iexpressPath)) {
    throw "Windows IExpress was not found: $iexpressPath"
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
if (Test-Path -LiteralPath $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Push-Location $projectRoot
try {
    & (Join-Path $projectRoot "build.ps1")
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath (Join-Path $projectRoot "CodexQuotaMonitor.exe") -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md"), (Join-Path $projectRoot "LICENSE"), (Join-Path $projectRoot "codex-feishu-hook.ps1") -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $installerRoot "install.cmd"), (Join-Path $installerRoot "install.ps1"), (Join-Path $installerRoot "uninstall.ps1") -Destination $stagingDir -Force

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles

[SourceFiles]
SourceFiles0=$stagingDir\

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=

[Strings]
InstallPrompt=Please follow the setup instructions.
DisplayLicense=
FinishMessage=Installation completed. Codex Quota Monitor will start in the notification area.
TargetName=$outputPath
FriendlyName=Codex Quota Monitor Setup
AppLaunched=install.cmd
FILE0="CodexQuotaMonitor.exe"
FILE1="README.md"
FILE2="LICENSE"
FILE3="codex-feishu-hook.ps1"
FILE4="install.cmd"
FILE5="install.ps1"
FILE6="uninstall.ps1"
"@
Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

$iexpressProcess = Start-Process -FilePath $iexpressPath -ArgumentList @('/N', $sedPath) -Wait -PassThru
if ($iexpressProcess.ExitCode -ne 0) {
    throw "IExpress failed with exit code $($iexpressProcess.ExitCode)."
}
if (-not (Test-Path -LiteralPath $outputPath)) {
    throw "IExpress did not create the installer: $outputPath"
}

Write-Output $outputPath
