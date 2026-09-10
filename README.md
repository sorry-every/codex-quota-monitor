# Codex 账号额度状态栏监控工具

一个轻量的 Windows 托盘工具，用于读取当前 Codex 登录账号的剩余额度，并在通知区域以圆环数字显示。

## 功能

- 读取当前 Codex 账号的剩余额度、账号、套餐和额度窗口。
- 在 Windows 任务栏通知区域显示圆环数字。
- 主窗口显示额度百分比、额度周期和重置时间。
- 每分钟自动刷新，也支持手动刷新。
- 关闭主窗口后继续驻留托盘，右键可以打开、刷新或退出。
- 主界面提供“开机自启”和“静默启动”按钮，托盘右键菜单也可以修改这两个设置。
- 自动识别 Codex CLI 和 Codex 桌面版安装路径。
- 可选配置飞书机器人，在 Codex 任务完成后发送通知。

## 使用方法

1. 先安装 Codex，并在当前 Windows 用户下登录 ChatGPT 账号。
2. 从 GitHub Releases 下载 `CodexQuotaMonitor-0.2.0-Setup.exe` 并运行安装程序。
3. 安装完成后，可以从开始菜单或桌面快捷方式启动。
4. 关闭主窗口后，程序会继续运行在 Windows 通知区域。

如果需要完全退出，请右键通知区域中的图标并选择“退出”。

## 启动设置

- **开机自启**：将当前程序加入当前 Windows 用户的启动项。
- **静默启动**：仅在开机自启时生效；开机后不显示主窗口，只在通知区域运行。手动双击程序仍会显示主窗口。
- 两个按钮同时位于主界面和托盘右键菜单，修改后立即生效。

## 系统要求

- Windows 10 或 Windows 11（x64）。
- 已安装并登录 Codex。
- 当前用户需要能够启动 Codex 的 `app-server`。

## Codex 程序路径识别

程序按以下顺序查找 Codex：

1. 环境变量 `CODEX_QUOTA_CODEX_PATH`。
2. npm 全局安装目录中的 Codex。
3. `%LOCALAPPDATA%\OpenAI\Codex\bin\**\codex.exe`。
4. Windows PATH 中的 `codex.exe`。

如果仍然提示找不到 `codex.exe`，可以手动指定路径：

```powershell
[Environment]::SetEnvironmentVariable(
  "CODEX_QUOTA_CODEX_PATH",
  "C:\Users\你的用户名\AppData\Local\OpenAI\Codex\bin\版本目录\codex.exe",
  "User"
)
```

设置后重新启动本工具。

## 飞书通知

飞书通知是可选功能。程序设置页可以保存飞书自定义机器人的 Webhook，并在 Codex 任务完成或失败时发送通知。

Webhook 只保存在当前用户的：

```text
%APPDATA%\CodexQuotaMonitor\config.json
```

通知脚本为 `codex-feishu-hook.ps1`。如果不使用飞书通知，可以忽略这个文件。

## 隐私与安全

- 本工具不保存 Codex 登录令牌。
- 额度通过本机 Codex `app-server` 获取。
- 源码仓库不包含账号配置、登录凭据、Webhook、日志或诊断结果。
- 启用飞书通知后，任务状态和最后一条回复会按飞书机器人的配置发送到对应群聊。

## 从源码构建

项目使用 Windows 自带的 .NET Framework C# 编译器构建，不需要额外安装 .NET SDK：

```powershell
.\build.ps1
```

构建产物为：

```text
CodexQuotaMonitor.exe
```

生成 Windows 安装程序（使用 Windows 自带的 IExpress）：

```powershell
.\installer\build-installer.ps1
```

安装程序输出到：

```text
release\CodexQuotaMonitor-0.2.0-Setup.exe
```

安装为当前用户目录，不需要管理员权限；卸载入口位于 Windows 的“应用和功能”列表中。

## 开源协议

本项目采用 MIT License，详见 [LICENSE](LICENSE)。
