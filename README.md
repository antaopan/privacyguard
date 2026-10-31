# 隐私卫士 PrivacyGuard

[![Release](https://img.shields.io/github/v/release/antaopan/privacyguard?display_name=tag&sort=semver)](https://github.com/antaopan/privacyguard/releases)
[![License: MIT](https://img.shields.io/github/license/antaopan/privacyguard)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](https://github.com/antaopan/privacyguard)

面向 Windows 桌面 AI 应用的本地隐私闸门：**网络隔离 + 系统 AI 数据采集加固 + 环境指纹伪装 + 敏感环境变量剥离**。

> 适用场景：你希望 Claude、Gemini、Codex 等 AI 应用在你这台电脑上“少拿数据、少联网、少留指纹”，同时仍然保留一个可审计、可随时暂停的控制台。**自动处理范围只有 Claude / Gemini / Codex，其他 AI 应用必须手动加入黑名单。**
>
> **重要边界**：本程序不是内核沙箱，也不是杀毒软件。任意以你的用户身份运行的 Win32 程序，仍然可以直接调用系统 API 读取时区、语言、本机 IP。PrivacyGuard 做的是“阻断网络外发 + 降低默认采集成功率”，而不是对所有读取行为做内核级拒绝。真正的对抗性隔离请使用独立 Windows 账户、Windows Sandbox 或虚拟机，详见[安全模型](#安全模型与边界必读)。

---

## 功能

| 能力 | 实现方式 |
| --- | --- |
| 开机自启动 | 后台服务自动启动；登录时通过计划任务以最高权限启动托盘界面（`--minimized`） |
| 应用黑名单 | 自动发现并纳入 **Claude / Gemini / Codex**；其他应用通过“添加程序”手动加入。按 exe / 整个安装目录管理 |
| 网络隔离（IP 防护） | 通过 Windows 防火墙（WFP）为每个可执行文件创建入站 / 出站阻断规则 |
| 完全断网 | 阻断入站 + 出站，应用无法把 IP、时区、语言、文件内容发往远程 |
| 仅阻止入站 | 允许应用联网，但阻止其他机器通过本机 IP 连入其监听端口 |
| 仅阻止局域网 | 阻断对本机所在局域网的访问，保留互联网访问（可能影响路由器 DNS） |
| 时区伪装 | 以 `TZ=UTC` 启动目标应用；Node 24 与 Electron 45 的主进程 / 渲染进程均已实测跟随 |
| 语言伪装 | 注入 `LANG` / `LC_ALL` / `LANGUAGE`，并为 Chromium / Electron 附加 `--lang` / `--accept-lang`；**部分生效** |
| 能力自检 | 启动器页“运行环境自检”或 `--verify`，报告系统语言读取、子进程 `TZ` / `LANG`、Node `Intl` 结果 |
| 密钥剥离 | 移除 `OPENAI_API_KEY`、`ANTHROPIC_API_KEY`、`AWS_*`、`*TOKEN*` 等常见凭据变量 |
| 标准用户启动 | 从 Explorer 复制访问令牌启动应用，避免 AI 应用继承管理员权限（UAC 正常时） |
| 进程监控 | 服务每 1.5 秒扫描黑名单进程，记录并可选终止；新出现的子程序会即时补规则 |
| AI 采集加固 | Recall、Copilot、剪贴板历史/云同步、广告 ID、活动历史、云搜索等本机策略 |
| 剪贴板守卫 | AI 应用在前台时，检测剪贴板中的疑似 API Key / 私钥并清除 |
| 审计日志 | 拦截、启动、策略、服务事件写入本地日志，可在界面查看 / 导出 CSV |
| 一键暂停 / 隔离 | 暂停时移除规则并恢复系统设置；立即隔离会终止匹配进程并重新启用防护 |

---

## 安装

### 系统要求

- Windows 10 / 11 / Server 2019+，**x64**
- .NET Framework 4.8（Windows 10 1903+ 与 Windows 11 默认包含）
- 管理员权限
- 建议保持 UAC 开启，以便隐私启动器能取得标准用户令牌

### 从发布包安装

1. 解压 `PrivacyGuard-1.1.0-win-x64.zip` 到任意目录。
2. 右键 `install.ps1` → **使用 PowerShell 运行**；或在管理员 PowerShell 中执行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

3. 安装程序会复制到 `C:\Program Files\PrivacyGuard`，注册后台服务、登录自启任务、开始菜单快捷方式和卸载登记。
4. 首次启动会自动发现并纳入 Claude / Gemini / Codex（默认“仅阻止入站 + 隐私启动器”）。其他 AI 应用需进入 **应用黑名单 → 添加程序** 手动选择 exe。

可选参数：

```powershell
.\install.ps1 -NoStart           # 安装后不自动打开界面
.\install.ps1 -DesktopShortcut   # 额外创建桌面快捷方式
```

> 发布包未做商业代码签名，Windows SmartScreen 可能提示“未知发布者”。可先核对 `SHA256SUMS.txt`，或自行从源码构建。

### 卸载

- 设置 → 应用 → 安装的应用 → **PrivacyGuard 隐私卫士** → 卸载；或
- 在安装目录运行：

  ```powershell
  powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
  # 保留日志与配置：
  powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -KeepData
  ```

卸载会停止服务、移除登录任务/快捷方式/防火墙规则，并恢复本程序修改过的系统隐私策略。程序目录会在进程退出后删除。

---

## 快速开始

1. **自动范围**：Claude / Gemini / Codex 会自动加入黑名单；其他应用点击“添加程序”手动加入。桌面版默认范围是“整个安装目录”（能覆盖 Squirrel 版本目录），Codex 原生 exe 使用单文件规则。
2. **选择网络策略**：
   - 云端 AI 需要联网 → 保持自动默认的 **仅阻止入站**，或选 **仅阻止局域网**；
   - 需要彻底阻断外发 → 选 **完全断网**（此时云端 AI 不能工作，本地模型仍可使用）。
3. **系统加固**：点“应用推荐配置”，或逐项开启。默认推荐项为 Recall、Copilot、剪贴板云同步、广告 ID、活动历史、云搜索。
4. **隐私启动器**：选择应用 → “以隐私模式启动”。它会以 `UTC / en-US / 密钥剥离 / 中立工作目录` 启动。
5. **能力自检**：启动器页 → “运行环境自检”，或命令行 `PrivacyGuard.exe --verify --report <文件> --quiet`。报告会显示系统语言读取值、子进程 TZ/LANG、Node Intl 时区与 locale。
6. **验证**：总览页显示服务、防火墙、规则数量与拦截次数；“立即隔离”可随时终止所有匹配进程。

服务在开机时自动运行；托盘界面在登录时自动启动。关闭窗口默认只最小化到托盘，不停止防护。

---

## 自动处理范围

只有以下三类应用会被主动发现、自动纳入黑名单，并默认启用“仅阻止入站 + 隐私启动器”：

| 应用 | 检测方式 |
| --- | --- |
| **Claude** | `AnthropicClaude`、`Programs\Claude`、npm `@anthropic-ai/claude-code` 等常见路径；运行进程名 `claude` |
| **Gemini** | `%LOCALAPPDATA%\Google\Gemini`、`Programs\Gemini`；运行进程名 `gemini` |
| **Codex** | `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`、npm `@openai/codex` 原生 exe；运行进程名 `codex` |

- 自动纳入只在界面启动时执行一次；用户移除后不会再次自动加回（`autoAdded` 会记住已处理类型）。
- Codex 的版本目录带哈希，升级后路径变化时，自动规则会在下次启动时更新。
- **其他 AI 应用一律不自动加入**：ChatGPT、Qwen、Kimi、Ollama、LM Studio、Cursor、Windsurf、Poe 等都必须通过“添加程序”手动选择 exe。
- 自动纳入默认仅阻止入站，避免云端 AI 完全断网；需要阻断外发时，在应用行把网络策略改为“完全断网”。

---

## 网络策略说明

| 模式 | 入站 | 出站 | 云端 AI | 典型用途 |
| --- | --- | --- | --- | --- |
| 完全断网 | 阻断 | 阻断 | 不可用 | 本地模型、离线阅读、彻底防止外发 |
| 仅阻止局域网 | 阻断 | 仅阻断 `LocalSubnet` | 通常可用 | 防止 AI 应用扫描/访问本机局域网服务 |
| 仅阻止入站 | 阻断 | 允许 | 可用 | 防止其他机器通过本机 IP 连入，但允许云端 |
| 不拦截 | — | — | 可用 | 只使用隐私启动器 / 进程监控 |

规则按**可执行文件完整路径**创建。选择“整个安装目录”时，会为该目录下（深度 ≤2，最多 80 个）的 `.exe` 创建规则，并在服务巡检时为新增 exe 补规则。Squirrel 更新后的新版本目录需要等待一次巡检，或点击“立即重新应用”。

> 已知边界：Windows 防火墙通常不过滤**本机回环/同机 IP** 流量，因此本机代理、回环服务或同机端口转发可能成为旁路。完全断网也不能阻止应用在本地读取 IP 后写入日志，只能阻止它把结果发出去。

---

## 隐私启动器细节

启动时注入：

```text
TZ=UTC
LANG=en_US.UTF-8
LC_ALL=en_US.UTF-8
LANGUAGE=en_US:en
PRIVACYGUARD_SHIELD=1
```

可选：

- `COMPUTERNAME=<你填写的伪装名>`（只影响读取环境变量的应用，不改变 `GetComputerName`）
- Chromium / Electron：追加 `--lang=<语言>` 与 `--accept-lang=<语言>`
- 剥离常见 API Key / Token / Secret / Password / 私钥路径相关环境变量
- 剔除 `ELECTRON_RUN_AS_NODE` 与 `NODE_OPTIONS`，避免宿主环境干扰 Electron 启动
- 中立工作目录：`%ProgramData%\PrivacyGuard\work\<应用名>`

已验证：

- **时区**：Node 24 的 `Intl` 时区跟随 `TZ=UTC`；Electron 45.2.1 的主进程和渲染进程 `Intl` 时区均变为 `UTC`。
- **语言环境**：子进程可见 `LANG/LC_ALL/LANGUAGE`，且敏感变量被剥离。
- **Electron 语言**：`--lang=en-US` 可把 `app.getLocale()`、渲染进程 `navigator.language`、`Intl` locale 变为 `en-US`。
- **语言限制**：`LANG` 单独不会改变 Node/Electron 主进程 locale；`navigator.languages`、`app.getPreferredSystemLanguages()`、`getLocaleCountryCode()` 和原生 Win32 语言 API 仍可能泄漏系统值。
- **Gemini 实测**：Gemini 1.17.0 桌面版带 `--lang=en-US` 启动后，页面内容仍按账号/地区显示中文；应用还拒绝 `--remote-debugging-port`，因此无法直接读取其内部时区。
- **Claude 实测**：本测试机没有 Claude 桌面 exe，只有浏览器中的 Claude 网页；无法对浏览器标签做 per-app 防火墙/启动器验证。
- 启动器会复制 Explorer 的访问令牌；本测试机 UAC 关闭，因此进程仍是管理员，界面会明确警告；UAC 正常时应为标准用户。

完整测试数据、版本和复现步骤见 [VERIFICATION.md](VERIFICATION.md)。**结论：时区修改是可验证的；语言只完成环境层注入与 Electron 渲染层部分伪装，不是完整语言隔离。**

**未验证/不保证**：并非所有 Electron 版本的 UI 语言都跟随 `--lang`；原生 Win32 程序的 `GetTimeZoneInformation` / `GetUserDefaultUILanguage` 通常**不会**被 `LANG` 改变；也不能保证应用读取 `COMPUTERNAME` 环境变量——很多程序会直接调用 `GetComputerName`。

---

## 系统加固项

所有加固均为 **HKLM 计算机级策略**，暂停防护或卸载时恢复原值。程序会在首次写入前把原值备份到 `C:\ProgramData\PrivacyGuard\backup-machine.json`。

| 项目 | 策略 / 键值 |
| --- | --- |
| Windows Recall / AI 数据采集 | `Policies\Microsoft\Windows\WindowsAI` → `DisableAIDataAnalysis=1`、`AllowRecallEnablement=0` |
| Windows Copilot | `Policies\Microsoft\Windows\WindowsCopilot` → `TurnOffWindowsCopilot=1` |
| 剪贴板历史 / 云同步 | `Policies\Microsoft\Windows\System` → `AllowClipboardHistory=0`、`AllowCrossDeviceClipboard=0` |
| 广告 ID | `Policies\Microsoft\Windows\AdvertisingInfo` → `DisabledByGroupPolicy=1` |
| 活动历史 | `Policies\Microsoft\Windows\System` → `EnableActivityFeed=0`、`PublishUserActivities=0`、`UploadUserActivities=0` |
| 云搜索 | `Policies\Microsoft\Windows\Windows Search` → `AllowCloudSearch=0`、`ConnectedSearchUseWeb=0` |
| 诊断数据（可选） | `Policies\Microsoft\Windows\DataCollection` → `AllowTelemetry=0` |
| 位置（可选） | `Policies\Microsoft\Windows\LocationAndSensors` → `DisableLocation=1` + ConsentStore |
| 摄像头/麦克风（可选） | `Policies\Microsoft\Windows\AppPrivacy` → `LetAppsAccessCamera/Microphone=2` + ConsentStore |

注意：

- 位置、摄像头/麦克风会影响地图、会议和通话，默认关闭；
- 部分设置需要重新登录或重启 Explorer 才完全生效；
- 企业环境可能由组织策略覆盖 `AllowTelemetry` 等值。

---

## 自动启动与进程模型

```text
Windows 启动
  └─ 服务 PrivacyGuard.exe --service（LocalSystem，自动启动）
       ├─ 应用防火墙规则
       ├─ 应用 / 巡检系统加固
       ├─ 监控黑名单进程
       └─ 写 status.json
用户登录
  └─ 计划任务 PrivacyGuard（最高权限、交互式）
       └─ PrivacyGuard.exe --minimized（托盘界面）
```

- 服务被配置为崩溃自动重启（5s / 15s / 60s，重置周期 1 天）。
- 界面退出不会停止服务；服务被停止时会移除防火墙规则并恢复系统策略。
- 界面第二次启动会通过 `Global\PrivacyGuard.Show` 事件唤醒已运行实例，而不是开第二个窗口。

---

## 数据目录

```text
C:\ProgramData\PrivacyGuard\
  config.json              # 当前配置（ACL：SYSTEM/Administrators 写，Users 读）
  status.json              # 服务状态（界面轮询）
  command.json             # 界面 → 服务的控制命令
  backup-machine.json      # 加固前注册表备份
  shield-pids.json         # 隐私启动器 PID 白名单
  stats.json               # 拦截计数
  logs\
    service-YYYYMMDD.log
    ui-YYYYMMDD.log
  backups\                 # 损坏配置备份
  work\                    # 中立工作目录
```

日志默认保留 30 天。界面“防护日志”支持按级别过滤、搜索、打开目录和导出 CSV。

---

## 从源码构建

需要：

- Windows x64
- .NET Framework 4.8（运行时 + MSBuild 4.8 工具链；或者安装 .NET Framework 4.8 Developer Pack）
- PowerShell 5.1+

在仓库根目录运行：

```powershell
.\build.ps1
```

输出：

```text
dist\PrivacyGuard\PrivacyGuard.exe
dist\PrivacyGuard\install.ps1
dist\PrivacyGuard\uninstall.ps1
dist\PrivacyGuard\README.md
dist\PrivacyGuard\LICENSE
dist\PrivacyGuard-1.1.0-win-x64.zip
dist\SHA256SUMS.txt
```

也可以直接构建：

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe' .\PrivacyGuard.csproj /p:Configuration=Release
```

源码结构：

```text
Program.cs                 入口（UI / service / install / uninstall）
Core\
  Models.cs                配置、状态、规则数据模型
  ConfigStore.cs           JSON 配置与命令
  StatusStore.cs           状态 / 统计 / 启动器 PID
  FirewallManager.cs       Windows 防火墙 COM 规则
  PolicyManager.cs         HKLM 加固策略与备份恢复
  Engine.cs                服务主循环
  ShieldLauncher.cs        标准用户令牌 + 环境变量启动
  ShieldVerification.cs    --verify / 界面自检：TZ、LANG、系统语言读取
  ClipboardGuard.cs        剪贴板密钥检测
  AppDiscovery.cs          仅 Claude / Gemini / Codex 的自动发现
  AutoEnrollment.cs        三应用自动纳入与路径更新
  Installer.cs             服务、计划任务、快捷方式、卸载登记
  PrivacyGuardService.cs   ServiceBase 宿主
UI\                        WPF 控制台
Tools\Probe\               测试用探针
Tools\Harness\             测试驱动
Tools\Verify\electron-probe\  Electron 时区 / 语言验证材料
VERIFICATION.md            实测数据与复现步骤
```

测试驱动：

```powershell
# 需要先构建，且把 bin\Release\PrivacyGuard.exe 复制到 Tools\Harness\
$env:NODE_EXE = (Get-Command node).Source
.\Tools\Harness\PrivacyGuard.Harness.exe all
```

覆盖：防火墙出站阻断与恢复、Explorer 令牌启动（本测试机 UAC 关闭）、环境变量注入/剥离、Node 时区、策略备份/恢复、配置 round-trip、三应用自动发现。

---

## 安全模型与边界（必读）

### 能做到

- 以 **Windows 防火墙的 WFP 内核规则**阻断指定路径应用的全部/部分网络流量；这是真实的内核级网络阻断，不是用户态钩子。
- 以 **HKLM 策略**关闭 Recall、Copilot、剪贴板云同步等系统级采集入口，并支持恢复。
- 为通过启动器启动的应用提供 **UTC / en-US / 密钥剥离 / 标准用户令牌** 的默认环境。
- 记录黑名单进程活动，可选终止，并在服务重启后自动恢复规则。

### 不能做到

1. **不能阻止任意 Win32 程序在本地读取时区、语言、IP。** 这些信息对当前用户是全局可读的；要按进程拒绝读取，需要内核驱动 / API 钩子 / 容器 / 虚拟机。PrivacyGuard 没有注入 DLL，也不安装驱动。
2. **不能保证所有应用都遵循 `TZ` / `LANG` / `--lang`。** 这是“兼容模式”伪装，不是安全边界。原生应用、Java、.NET、部分 Electron 版本可能仍读取系统值。
3. **完全断网不等于本机隔离。** Windows 防火墙通常不处理回环流量；本机代理、端口转发、同机 IP 访问可能绕过。需要把应用放进无网络命名空间的 VM / Sandbox。
4. **进程监控是轮询的（约 1.5 秒）。** 短命进程可能在两次扫描之间启动并退出；网络规则仍然有效，但“检测到/终止”事件可能漏记。
5. **不能对抗管理员权限的恶意程序。** 服务以 LocalSystem 运行，但本机管理员可以停止服务、修改配置或替换程序。生产环境请把安装目录放在 `Program Files` 并限制写权限。
6. **剪贴板守卫是启发式。** 只匹配常见 API Key / Token / 私钥格式，可能漏报或误清除；它无法阻止应用直接调用 `GetClipboardData`。
7. **系统策略不是“卸载即无痕”。** 暂停/卸载会恢复备份值，但 Windows 组件自身的历史缓存、事件日志、Recall 快照可能仍需手动清理。

### 对高风险 AI 的建议

- 把 AI 应用安装在**独立 Windows 标准账户**下，用该账户运行；不要用管理员账户。
- 更严格：使用 **Windows Sandbox**（可配置 `Networking=Disable`）或 Hyper-V 虚拟机，只映射必要的只读文件夹。
- 不要把整个用户目录、浏览器 Profile、`.ssh`、云盘目录映射进沙箱。
- 对本地模型（Ollama / LM Studio）可以选“完全断网”；对云端模型至少选“仅阻止入站”，并配合隐私启动器。

---

## 故障排查

**服务未运行 / 界面一直显示“未安装”**

```powershell
Get-Service PrivacyGuard
Get-Content C:\ProgramData\PrivacyGuard\logs\service-$(Get-Date -Format yyyyMMdd).log -Tail 40
```

尝试“设置 → 安装 / 修复服务”；或重新运行 `install.ps1`。

**规则数量为 0 / 防火墙未启用**

- Windows 安全中心 → 防火墙与网络保护，确认域/专用/公用三个配置文件都已开启；
- 第三方防火墙可能接管了规则，先退出第三方防火墙再测试。

**应用仍然能联网**

- 确认应用路径与被规则路径一致（Squirrel 更新会换目录）；
- 选择“整个安装目录”并点击“立即重新应用”；
- 检查是否有本机代理/回环转发；完全断网不拦截回环。

**隐私启动器启动失败**

- 需要当前桌面有 `explorer.exe`；
- 某些应用要求管理员权限，标准用户令牌会导致失败；这类应用不适合用启动器；
- 若 UAC 被关闭，Explorer 令牌本身是管理员，界面会给出警告。

**时区没有变成 UTC**

- 该应用可能不读取 `TZ`；用隐私启动器启动 Node/Electron 系应用验证；
- Windows 原生 `GetTimeZoneInformation` 不受 `TZ` 影响，这是已知边界。

---

## License

MIT。见 [LICENSE](LICENSE)。



