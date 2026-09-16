$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /main:QuotaSelectionTests /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /out:tests\QuotaSelectionTests.exe Program.cs tests\QuotaSelectionTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & .\tests\QuotaSelectionTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
} finally { Pop-Location }
