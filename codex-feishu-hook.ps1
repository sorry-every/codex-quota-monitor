# Codex Feishu notification hook for CodexQuotaMonitor.
# Codex calls this script with the JSON event as its final command-line argument.

$ErrorActionPreference = "Stop"

$configDir = Join-Path $env:APPDATA "CodexQuotaMonitor"
$configPath = Join-Path $configDir "config.json"
$logPath = Join-Path $configDir "feishu-notifier.log"

function Write-HookLog {
    param([string]$Message)
    try {
        if (-not (Test-Path -LiteralPath $configDir)) {
            New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        }
        Add-Content -LiteralPath $logPath `
            -Value "$(Get-Date -Format o) $Message" `
            -Encoding UTF8
    } catch {
    }
}

$rawEvent = ""
if (-not [string]::IsNullOrWhiteSpace($env:CODEX_QUOTA_TEST_EVENT)) {
    $rawEvent = $env:CODEX_QUOTA_TEST_EVENT
} elseif ($args.Count -gt 0) {
    # On Windows, a long JSON argument can arrive split across argv entries.
    # Joining preserves the payload while still supporting the normal one-argument case.
    $rawEvent = [string]::Join(" ", [string[]]$args)
} else {
    # Stdin support makes the hook usable with wrappers and older integrations too.
    try { $rawEvent = [Console]::In.ReadToEnd() } catch { $rawEvent = "" }
}

if ([string]::IsNullOrWhiteSpace($rawEvent)) {
    Write-HookLog "Codex called hook without an event payload."
    exit 0
}

try {
    $event = $rawEvent | ConvertFrom-Json
    Write-HookLog "Received Codex event: $([string]$event.type)."
} catch {
    Write-HookLog "Invalid Codex event JSON: $($_.Exception.Message) PayloadLength=$($rawEvent.Length)"
    exit 0
}

$eventType = [string]$event.type
if (-not [string]::IsNullOrWhiteSpace($eventType) -and
    $eventType -ne "agent-turn-complete") {
    Write-HookLog "Ignored Codex event: $eventType."
    exit 0
}

try {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $webhook = [string]$config.webhook
    if ([string]::IsNullOrWhiteSpace($webhook)) {
        throw "Webhook is empty."
    }

    $lastMessage = [string]$event.'last-assistant-message'
    if ([string]::IsNullOrWhiteSpace($lastMessage)) {
        $lastMessage = "Codex 本次任务已完成。"
    }

    $taskName = ""
    $inputMessages = $event.'input-messages'
    if ($null -ne $inputMessages -and $inputMessages.Count -gt 0) {
        $taskName = [string]$inputMessages[0]
    }
    if ([string]::IsNullOrWhiteSpace($taskName)) {
        $taskName = "未命名任务"
    }
    $taskName = ($taskName -replace "\s+", " ").Trim()
    if ($taskName.Length -gt 120) {
        $taskName = $taskName.Substring(0, 120) + "…"
    }

    $executionStatus = "执行完成"
    $statusValue = ([string]$event.status).ToLowerInvariant()
    $resultValue = ([string]$event.result).ToLowerInvariant()
    $errorValue = [string]$event.error
    if (
        $statusValue -match "fail|error|exception|cancel|abort|失败|错误|异常" -or
        $resultValue -match "fail|error|exception|失败|错误|异常" -or
        -not [string]::IsNullOrWhiteSpace($errorValue) -or
        $lastMessage -match "(?im)^\s*(error|failed|failure|exception|traceback|错误|失败|报错|异常)\s*[:：]"
    ) {
        $executionStatus = "执行失败"
    }

    if ($lastMessage.Length -gt 6000) {
        $lastMessage = $lastMessage.Substring(0, 6000) + "`n…内容已截断"
    }

    $text = @"
$executionStatus
项目：$taskName
结果：
$lastMessage
"@

    $payload = @{
        msg_type = "text"
        content = @{
            text = $text
        }
    } | ConvertTo-Json -Compress -Depth 5

    $body = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $response = Invoke-RestMethod `
        -Uri $webhook `
        -Method POST `
        -ContentType "application/json; charset=utf-8" `
        -Body $body `
        -TimeoutSec 10

    if ($null -ne $response.code -and [int]$response.code -ne 0) {
        throw "Feishu returned code $($response.code): $($response.msg)"
    }

    Write-HookLog "Feishu notification sent."
} catch {
    Write-HookLog "Notification failed: $($_.Exception.Message)"
    if ($env:CODEX_QUOTA_TEST_MODE -eq "1") {
        exit 1
    }
}

exit 0
