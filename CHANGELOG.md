# Changelog

All notable changes to PrivacyGuard are documented in this file.

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
