# 时区与语言能力验证

测试时间：2026-11-01（Asia/Shanghai）  
测试机：Windows Server 2025 x64（10.0.26100），UAC 关闭  
隐私卫士：1.1.1

## 结论摘要

| 能力 | 结果 | 说明 |
| --- | --- | --- |
| Node/Electron 主进程时区 | **PASS** | `TZ=UTC` → `Intl.DateTimeFormat().resolvedOptions().timeZone = UTC` |
| Electron 渲染进程时区 | **PASS** | 同一进程环境下渲染进程 `Intl` 时区也为 `UTC`，`Date` 显示 GMT+0000 |
| 原生 Win32 / .NET 时区 | **未改变** | 仍为 `China Standard Time`；`TZ` 不是系统级时区修改 |
| `LANG` 环境注入 | **PASS** | 子进程可见 `LANG=en_US.UTF-8`、`LC_ALL=en_US.UTF-8` |
| Node/Electron 主进程 locale | **FAIL（部分）** | `LANG` 不改变 ICU locale，主进程仍为 `zh-CN` |
| Electron 渲染进程语言 | **部分 PASS** | `--lang=en-US` 改变 `app.getLocale()`、`navigator.language`、`Intl.locale` |
| `navigator.languages` | **仍泄漏** | 仍为 `["en-US","zh-Hans-CN","ja"]` |
| `app.getPreferredSystemLanguages()` | **仍泄漏** | 仍为 `["zh-Hans-CN","ja"]`；`getLocaleCountryCode()` 仍为 `CN` |
| 原生 Windows 语言读取 | **仍可读** | UI 语言 LCID 2052，locale `zh-CN`；不随 `LANG` 改变 |
| Gemini 1.17.0 实际 UI | **未改成英文** | 带 `--lang=en-US` / `--accept-lang=en-US` 启动，页面仍显示中文 |
| Claude 桌面版 | **无法验证** | 本机没有 Claude 桌面 exe；Claude 仅在 Edge 网页中打开 |
| Codex CLI | **仅验证自动发现** | 原生 `codex.exe` 已纳入；CLI 没有可读的 UI locale 表面 |

**总结：时区修改是真实可验证的；语言只完成环境变量注入，Electron 渲染层可部分修改，完整语言隔离仍需独立 Windows 用户/区域/虚拟机。**

## 1. 原生 .NET / Windows 读取

`PrivacyGuard.exe --verify` 在系统层读取到：

```text
系统时区（.NET）：China Standard Time
系统 Culture：   zh-CN / UI zh-CN
系统 UI 语言：   2052（zh-CN）
```

这些值在子进程里也不会因为 `TZ` / `LANG` 改变。原因是 `LANG` 只影响显式读取环境变量的程序；Windows 的 `GetTimeZoneInformation`、`GetUserDefaultUILanguage`、`GetUserDefaultLocaleName` 读取的是系统 / 用户配置。

## 2. Node 24.20 测试

测试方式：用隐私启动器启动 `node.exe` 小程序，读取 `process.env` 与 `Intl`。

| 场景 | `TZ` | `Intl` 时区 | `LANG` | `Intl` locale |
| --- | --- | --- | --- | --- |
| 默认 | 空 | `Asia/Shanghai` | 空 | `zh-CN` |
| Shield | `UTC` | **`UTC`** | `en_US.UTF-8` | `zh-CN` |

结论：

- `TZ` 对 Node 主进程有效；
- `LANG` 没有改变 Node 的 ICU locale（这是 Node/Windows 的行为，不是注入失败）。

## 3. Electron 45.2.1 测试

使用最小 Electron 应用，分别测试默认、仅环境变量、环境变量 + `--lang=en-US`。

| 指标 | 默认 | 仅环境变量 | 环境变量 + `--lang` |
| --- | --- | --- | --- |
| 主进程 `mainIntlTZ` | `Asia/Shanghai` | **`UTC`** | **`UTC`** |
| 主进程 `mainIntlLocale` | `zh-CN` | `zh-CN` | `zh-CN` |
| `app.getLocale()` | `zh-CN` | `zh-CN` | **`en-US`** |
| 渲染 `navigator.language` | `zh-CN` | `zh-CN` | **`en-US`** |
| 渲染 `Intl.locale` | `zh-CN` | `zh-CN` | **`en-US`** |
| 渲染 `Intl.timeZone` | `Asia/Shanghai` | **`UTC`** | **`UTC`** |
| `navigator.languages` | `zh-CN, zh-Hans-CN, ja` | `zh-CN, zh-Hans-CN, ja` | `en-US, zh-Hans-CN, ja` |
| `app.getPreferredSystemLanguages()` | `zh-Hans-CN, ja` | `zh-Hans-CN, ja` | `zh-Hans-CN, ja` |
| `app.getLocaleCountryCode()` | `CN` | `CN` | `CN` |

重要观察：

1. `TZ=UTC` 对 Electron **主进程和渲染进程**都生效。
2. `LANG` 不足以改变 Electron locale。
3. `--lang=en-US` 能把主进程 `app.getLocale()`、渲染进程 `navigator.language` 和 `Intl` 改为 `en-US`。
4. `--accept-lang` 在本版本没有改变 `navigator.languages`；它可能影响 HTTP `Accept-Language`，但不是完整语言伪装。
5. 即使 `--lang` 生效，`navigator.languages`、`app.getPreferredSystemLanguages()`、`app.getLocaleCountryCode()` 仍会泄漏系统偏好。

## 4. Gemini 1.17.0 实际应用

安装位置：

```text
%LOCALAPPDATA%\Google\Gemini\app-1.17.0\Gemini.exe
```

测试命令（等价于隐私启动器注入）：

```powershell
$env:TZ='UTC'
$env:LANG='en_US.UTF-8'
$env:LC_ALL='en_US.UTF-8'
$env:LANGUAGE='en_US:en'
Gemini.exe --lang=en-US --accept-lang=en-US --no-first-run
```

结果：

- 进程正常启动，未因 `--lang` / `--accept-lang` 拒绝；
- 窗口标题和页面内容仍为中文（“获取创意灵感，提升工作效率”等）；
- Gemini 安全策略拒绝 `--remote-debugging-port`，日志明确写入：
  `refusing to start — a debugging or network-override switch is present`；
- 因此无法通过 DevTools 直接读取 Gemini 内部 `Intl` / `navigator` 值。

结论：**Gemini 的网页内容语言没有因为 `--lang` 改变**。页面语言很可能由 Google 账号/地区/服务器决定，而不是 Chromium 语言开关。时区理论上由 Electron 继承 `TZ`，但没有在 Gemini 内部直接读取验证。

## 5. Claude / Codex

- **Claude**：测试机没有 Claude Desktop 的 `.exe`，只有 Edge 中的 Claude 网页。浏览器标签无法按应用创建防火墙规则，也无法用启动器注入环境；未做结论性验证。
- **Codex**：已发现并自动纳入原生 `codex.exe`（`%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`），但 Codex CLI 没有 UI locale；时区/语言行为未做直接读取验证。

## 6. 如何复现 Node 自检

```powershell
PrivacyGuard.exe --verify --report .\verification.txt --quiet
```

输出包含系统语言读取值、子进程 `TZ` / `LANG` / `LC_ALL`、Node `Intl` 时区与 locale。也可在界面的“隐私启动器 → 运行环境自检”执行。

Electron 复现材料在 [Tools/Verify/electron-probe](Tools/Verify/electron-probe/)：

```powershell
cd Tools/Verify/electron-probe
npm install electron@45.2.1 --no-save
$env:PG_OUT="$PWD\out.json"
$env:TZ='UTC'; $env:LANG='en_US.UTF-8'; $env:LC_ALL='en_US.UTF-8'
.\node_modules\electron\dist\electron.exe . --no-sandbox --lang=en-US
Get-Content .\out.json
```

## 7. 产品决策

- 对外只说“时区伪装已验证”，不把语言伪装描述成完整能力。
- 语言能力标记为“部分”：环境注入 + Electron 渲染层，不是系统语言隔离。
- 对高风险场景，推荐独立 Windows 标准用户（设置为 en-US 区域/语言）+ Windows Sandbox / 虚拟机，而不是依赖 `--lang`。

