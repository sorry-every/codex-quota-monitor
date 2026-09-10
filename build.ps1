$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Windows C# compiler was not found."
}

$source = Join-Path $projectRoot "Program.cs"
$manifest = Join-Path $projectRoot "app.manifest"
$output = Join-Path $projectRoot "CodexQuotaMonitor.exe"
$hook = Join-Path $projectRoot "codex-feishu-hook.ps1"

if (Test-Path -LiteralPath $hook) {
    $hookText = [System.IO.File]::ReadAllText($hook, [System.Text.Encoding]::UTF8)
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($hook, $hookText, $utf8Bom)
}

& $compiler `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /codepage:65001 `
    /win32manifest:$manifest `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    /out:$output `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Output $output
