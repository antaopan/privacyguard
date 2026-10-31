# Changelog

All notable changes to PrivacyGuard are documented in this file.

## [1.1.1] - 2026-11-01

### Fixed

- 启动器页“运行环境自检”改为更明确的次级按钮样式。
- 发布包 `SHA256SUMS.txt` 的文件名改为与解压后的 `PrivacyGuard.exe` 一致。

## [1.1.0] - 2026-11-01

### Changed

- 自动处理范围收窄为 **Claude / Gemini / Codex**。这三个应用会在界面启动时自动发现并纳入黑名单；其他 AI 应用必须手动添加。
- 自动纳入默认使用“仅阻止入站 + 隐私启动器”，避免云端 AI 完全断网；可逐项改为完全断网。
- Codex 自动支持原生 `codex.exe` 与版本哈希目录更新；安装路径变化时自动更新规则。
- 隐私启动器额外剥离 `ELECTRON_RUN_AS_NODE` 与 `NODE_OPTIONS`，并为 Chromium/Electron 附加 `--accept-lang`。
- 新增“运行环境自检”按钮与 `--verify` 命令行模式，验证 `TZ` 时区修改、`LANG` 注入和系统语言读取结果。
- 新增 `VERIFICATION.md`：记录 Node 24.20、Electron 45.2.1、Gemini 1.17.0、Codex 0.166.0 的实测结果。

### Known limitations

- 实测 Gemini 桌面版即使带 `--lang=en-US`，网页内容仍按账号/地区显示中文；语言不是完整伪装。
- `LANG` 只改变环境变量，不改变 Node/Electron 主进程的 ICU locale；Electron 渲染进程的 `navigator.language` / `Intl` 需要 `--lang`。
- `--lang` 不改变 `navigator.languages`、`app.getPreferredSystemLanguages()` 或原生 Win32 语言 API。
- TZ 时区修改已在 Node/Electron 主进程和渲染进程验证；实际 Gemini 进程的时区未通过 DevTools 直接读取（应用拒绝调试开关）。

## [1.0.0] - 2026-10-31

### Added

- WPF 托盘控制台：总览、应用黑名单、隐私启动器、系统加固、防护日志、设置。
- LocalSystem 后台服务：开机自动启动、崩溃自动重启、无界面持续防护。
- 按 exe / 安装目录创建 Windows 防火墙规则，支持完全断网、仅阻止局域网、仅阻止入站。
- 黑名单进程监控（约 1.5 秒轮询），可选终止；新出现的子程序会即时补规则。
- 隐私启动器：复制 Explorer 令牌、`TZ=UTC`、`LANG=en_US.UTF-8`、`--lang`、密钥环境变量剥离、中立工作目录。
- 系统加固：Windows Recall、Copilot、剪贴板历史/云同步、广告 ID、活动历史、云搜索；可选诊断数据、位置、摄像头/麦克风。
- 注册表原值备份与暂停/卸载恢复。
- 剪贴板密钥守卫：AI 应用在前台时识别并清除疑似 API Key / Token / 私钥。
- 已安装与运行中 AI 应用扫描（Claude、ChatGPT、Qwen、Kimi、Ollama、LM Studio 等）。
- 日志查看、筛选、CSV 导出；配置导出。
- 安装/卸载脚本、GitHub release 打包脚本、SHA256 校验和。

### Verified

- Windows 防火墙出站阻断与恢复。
- 服务安装、启动、停止、卸载；计划任务与登录启动项创建/清理。
- HKLM 策略写入与恢复（含 Recall 与广告 ID）。
- 环境变量注入/剥离；Node.js 24 `Intl` 时区在启动器下为 `UTC`。
- 已安装/运行中应用扫描。

### Known limitations

- 不会以内核驱动或 API 钩子阻止任意进程在本地读取时区、语言、IP；详见 README。
- Windows 防火墙通常不过滤回环/同机流量。
- `TZ` / `LANG` / `--lang` 是兼容模式伪装，不保证所有运行时遵循。
- 进程监控为轮询式，短命进程可能漏记。
- 发布包未做商业代码签名。

