$ErrorActionPreference = 'Stop'
$port = 18765
$dir = Join-Path $env:APPDATA 'CodexQuotaMonitor'
$configPath = Join-Path $dir 'config.json'
$configBackup = Join-Path $env:TEMP ('codex-quota-config-' + [Guid]::NewGuid().ToString('N') + '.bak')
$hadConfig = Test-Path -LiteralPath $configPath
if ($hadConfig) { Copy-Item -LiteralPath $configPath -Destination $configBackup -Force }
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$port/")
$listener.Start()
try {
    Set-Content -LiteralPath $configPath -Value ('{"webhook":"http://127.0.0.1:' + $port + '/hook"}') -Encoding utf8
    $hook = (Resolve-Path (Join-Path $PSScriptRoot '..\codex-feishu-hook.ps1')).Path
    $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $event = '{"type":"agent-turn-complete","turn-id":"argument-test","last-assistant-message":"参数回调测试"}'
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ps
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $hook, $event)) {
        $psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $requestTask = $listener.GetContextAsync()
    if (-not $requestTask.Wait(15000)) { throw 'Hook did not send a request.' }
    $context = $requestTask.Result
    $reader = [System.IO.StreamReader]::new($context.Request.InputStream, $context.Request.ContentEncoding)
    $body = $reader.ReadToEnd()
    $reader.Dispose()
    $responseBytes = [System.Text.Encoding]::UTF8.GetBytes('{"code":0,"msg":"ok"}')
    $context.Response.StatusCode = 200
    $context.Response.ContentType = 'application/json'
    $context.Response.OutputStream.Write($responseBytes, 0, $responseBytes.Length)
    $context.Response.Close()
    $proc.WaitForExit(15000)
    Write-Output "hook_exit=$($proc.ExitCode)"
    Write-Output "request_received=$([bool](-not [string]::IsNullOrWhiteSpace($body)))"
    Write-Output "payload_contains_task=$($body.Contains('参数回调测试'))"
    if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($body) -or -not $body.Contains('参数回调测试')) { throw 'Hook integration check failed.' }
}
finally {
    if ($listener.IsListening) { $listener.Stop() }
    $listener.Close()
    if ($hadConfig) { Move-Item -LiteralPath $configBackup -Destination $configPath -Force }
    else { Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $configBackup -Force -ErrorAction SilentlyContinue
}
