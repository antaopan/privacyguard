using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace PrivacyGuard
{
    public class ShieldLaunchResult
    {
        public bool Success { get; set; }
        public int Pid { get; set; }
        public bool ElevatedToken { get; set; }
        public string Message { get; set; }
        public string ExePath { get; set; }
        public string CommandLine { get; set; }
        public string TimeZone { get; set; }
        public string Language { get; set; }

        public ShieldLaunchResult()
        {
            Message = "";
            ExePath = "";
            CommandLine = "";
            TimeZone = "UTC";
            Language = "en-US";
        }
    }

    public static class ShieldLauncher
    {
        private static readonly Regex SensitiveName = new Regex(
            @"(API[_-]?KEY|APIKEY|TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|PRIVATE[_-]?KEY|ACCESS[_-]?KEY|CLIENT[_-]?SECRET|SESSION[_-]?KEY)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] SensitivePrefixes =
        {
            "OPENAI_", "ANTHROPIC_", "AZURE_", "AWS_", "GOOGLE_APPLICATION_CREDENTIALS",
            "GEMINI_", "GROQ_", "MISTRAL_", "COHERE_", "HUGGINGFACE_", "HF_",
            "DEEPSEEK_", "XAI_", "SUPABASE_", "DATABASE_URL", "SSH_AUTH_SOCK",
            "GPG_AGENT_INFO", "KRB5CCNAME", "NPM_TOKEN", "GITHUB_TOKEN", "GH_TOKEN"
        };

        public static ShieldLaunchResult Launch(AppRule app, LauncherConfig config, string extraArgs, bool hideWindow = false)
        {
            ShieldLaunchResult result = new ShieldLaunchResult();
            if (app == null || string.IsNullOrEmpty(app.Path))
            {
                result.Message = "应用路径为空";
                return result;
            }

            string exe = AppPaths.CleanPath(app.Path);
            result.ExePath = exe;
            if (!File.Exists(exe))
            {
                result.Message = "程序不存在：" + exe;
                return result;
            }

            if (config == null) config = new LauncherConfig();
            result.TimeZone = string.IsNullOrEmpty(config.TimeZone) ? "UTC" : config.TimeZone;
            result.Language = string.IsNullOrEmpty(config.Language) ? "en-US" : config.Language;

            IntPtr explorerProcess = IntPtr.Zero;
            IntPtr explorerToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr envBlock = IntPtr.Zero;

            try
            {
                int explorerPid;
                explorerProcess = OpenExplorerProcess(out explorerPid);
                if (explorerProcess == IntPtr.Zero)
                {
                    result.Message = "找不到当前桌面的 explorer.exe，无法以标准用户权限启动。请确认资源管理器正在运行。";
                    return result;
                }

                if (!NativeMethods.OpenProcessToken(explorerProcess,
                    NativeMethods.TOKEN_ASSIGN_PRIMARY | NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY,
                    out explorerToken))
                {
                    result.Message = "无法读取 explorer.exe 的访问令牌（" + NativeMethods.LastError() + "）";
                    return result;
                }

                if (!NativeMethods.DuplicateTokenEx(explorerToken, NativeMethods.TOKEN_ALL_ACCESS, IntPtr.Zero,
                    NativeMethods.SecurityImpersonation, NativeMethods.TokenPrimary, out primaryToken))
                {
                    result.Message = "无法复制用户访问令牌（" + NativeMethods.LastError() + "）";
                    return result;
                }

                result.ElevatedToken = IsTokenElevated(primaryToken);

                string workingDir = null;
                if (config.NeutralWorkingDir)
                {
                    workingDir = Path.Combine(AppPaths.WorkRoot, AppPaths.SafeFileName(app.Name));
                    Directory.CreateDirectory(workingDir);
                }

                Dictionary<string, string> environment = BuildEnvironment(config);
                envBlock = BuildEnvironmentBlock(environment);

                string commandLine = BuildCommandLine(exe, app, config, extraArgs);
                result.CommandLine = commandLine;

                NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                si.lpDesktop = "winsta0\\default";
                si.dwFlags = (int)NativeMethods.STARTF_USESHOWWINDOW;
                si.wShowWindow = hideWindow ? (short)0 : NativeMethods.SW_SHOWNORMAL;

                NativeMethods.PROCESS_INFORMATION pi;
                ShieldRegistry.Begin(exe);
                bool ok = NativeMethods.CreateProcessWithTokenW(
                    primaryToken,
                    NativeMethods.LOGON_WITH_PROFILE,
                    exe,
                    commandLine,
                    NativeMethods.CREATE_UNICODE_ENVIRONMENT | (hideWindow ? NativeMethods.CREATE_NO_WINDOW : 0),
                    envBlock,
                    workingDir,
                    ref si,
                    out pi);

                if (!ok)
                {
                    ShieldRegistry.Cancel(exe);
                    int error = Marshal.GetLastWin32Error();
                    result.Message = "以标准用户权限启动失败（错误 " + error + "：" + NativeMethods.LastError() + "）";
                    Logger.Error("launcher", "CREATE_FAILED", result.Message + " · " + exe);
                    return result;
                }

                try
                {
                    NativeMethods.CloseHandle(pi.hThread);
                    NativeMethods.CloseHandle(pi.hProcess);
                }
                catch
                {
                }

                ShieldRegistry.Complete(pi.dwProcessId, exe);
                result.Success = true;
                result.Pid = pi.dwProcessId;
                result.Message = result.ElevatedToken
                    ? "已启动，但当前用户令牌本身具有管理员权限；建议启用 UAC 以获得更完整的降权隔离。"
                    : "已以标准用户权限启动隐私模式";
                Logger.Info("launcher", "LAUNCHED",
                    string.Format("{0} · pid={1} · TZ={2} · LANG={3} · elevatedToken={4}",
                        app.Name, pi.dwProcessId, result.TimeZone, result.Language, result.ElevatedToken));
                return result;
            }
            catch (Exception ex)
            {
                ShieldRegistry.Cancel(exe);
                result.Message = ex.Message;
                Logger.Error("launcher", "LAUNCH_EXCEPTION", ex.ToString());
                return result;
            }
            finally
            {
                if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
                if (primaryToken != IntPtr.Zero) NativeMethods.CloseHandle(primaryToken);
                if (explorerToken != IntPtr.Zero) NativeMethods.CloseHandle(explorerToken);
                if (explorerProcess != IntPtr.Zero) NativeMethods.CloseHandle(explorerProcess);
            }
        }

        private static IntPtr OpenExplorerProcess(out int pid)
        {
            pid = 0;
            int session = Process.GetCurrentProcess().SessionId;
            Process[] processes = Process.GetProcessesByName("explorer");
            try
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        if (processes[i].SessionId != session) continue;
                        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processes[i].Id);
                        if (handle != IntPtr.Zero)
                        {
                            pid = processes[i].Id;
                            return handle;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try { processes[i].Dispose(); } catch { }
                }
            }
            return IntPtr.Zero;
        }

        private static bool IsTokenElevated(IntPtr token)
        {
            try
            {
                NativeMethods.TOKEN_ELEVATION elevation;
                int length;
                if (NativeMethods.GetTokenInformation(token, 20, out elevation,
                    Marshal.SizeOf(typeof(NativeMethods.TOKEN_ELEVATION)), out length))
                {
                    return elevation.TokenIsElevated != 0;
                }
            }
            catch
            {
            }
            return false;
        }

        private static Dictionary<string, string> BuildEnvironment(LauncherConfig config)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string key = Convert.ToString(entry.Key);
                string value = Convert.ToString(entry.Value);
                if (string.IsNullOrEmpty(key)) continue;
                if (config.StripSecrets && IsSensitive(key)) continue;
                env[key] = value;
            }

            string language = string.IsNullOrEmpty(config.Language) ? "en-US" : config.Language.Trim();
            if (language.Length < 2) language = "en-US";
            string posixLanguage = language.Replace("-", "_") + ".UTF-8";
            string primaryLanguage = language.Substring(0, 2).ToLowerInvariant();

            env["TZ"] = string.IsNullOrEmpty(config.TimeZone) ? "UTC" : config.TimeZone.Trim();
            env["LANG"] = posixLanguage;
            env["LC_ALL"] = posixLanguage;
            env["LC_MESSAGES"] = posixLanguage;
            env["LANGUAGE"] = language.Replace("-", "_") + ":" + primaryLanguage;
            env["PRIVACYGUARD_SHIELD"] = "1";
            env["PRIVACYGUARD_TIMEZONE"] = env["TZ"];
            env["PRIVACYGUARD_LANGUAGE"] = language;

            if (!string.IsNullOrEmpty(config.FakeComputerName))
            {
                env["COMPUTERNAME"] = config.FakeComputerName.Trim();
            }

            env.Remove("__COMPAT_LAYER");
            // 防止宿主环境把 Electron 应用强制当成 Node 运行，或通过 NODE_OPTIONS 注入预加载脚本。
            env.Remove("ELECTRON_RUN_AS_NODE");
            env.Remove("NODE_OPTIONS");
            return env;
        }

        private static bool IsSensitive(string name)
        {
            if (SensitiveName.IsMatch(name)) return true;
            for (int i = 0; i < SensitivePrefixes.Length; i++)
            {
                if (name.StartsWith(SensitivePrefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static IntPtr BuildEnvironmentBlock(Dictionary<string, string> env)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in env.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                lines.Add(pair.Key + "=" + (pair.Value ?? ""));
            }
            string block = string.Join("\0", lines.ToArray()) + "\0\0";
            char[] chars = block.ToCharArray();
            int bytes = (chars.Length + 1) * 2;
            IntPtr ptr = Marshal.AllocHGlobal(bytes);
            Marshal.Copy(chars, 0, ptr, chars.Length);
            Marshal.WriteInt16(ptr, chars.Length * 2, 0);
            return ptr;
        }

        private static string BuildCommandLine(string exe, AppRule app, LauncherConfig config, string extraArgs)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Quote(exe));
            if (config.ChromiumLang && AppMatcher.IsChromiumApp(exe))
            {
                string language = string.IsNullOrEmpty(config.Language) ? "en-US" : config.Language.Trim();
                sb.Append(" --lang=").Append(language);
                sb.Append(" --accept-lang=").Append(language);
            }

            if (!string.IsNullOrEmpty(config.ExtraArgs)) sb.Append(" ").Append(config.ExtraArgs.Trim());
            if (!string.IsNullOrEmpty(extraArgs)) sb.Append(" ").Append(extraArgs.Trim());
            return sb.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static bool IsCurrentProcessElevated()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static List<KeyValuePair<string, string>> Preview(LauncherConfig config)
        {
            if (config == null) config = new LauncherConfig();
            string language = string.IsNullOrEmpty(config.Language) ? "en-US" : config.Language.Trim();
            string posix = language.Replace("-", "_") + ".UTF-8";
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            list.Add(new KeyValuePair<string, string>("TZ", string.IsNullOrEmpty(config.TimeZone) ? "UTC" : config.TimeZone));
            list.Add(new KeyValuePair<string, string>("LANG", posix));
            list.Add(new KeyValuePair<string, string>("LC_ALL", posix));
            list.Add(new KeyValuePair<string, string>("LANGUAGE", language.Replace("-", "_")));
            if (!string.IsNullOrEmpty(config.FakeComputerName))
            {
                list.Add(new KeyValuePair<string, string>("COMPUTERNAME", config.FakeComputerName));
            }
            if (config.ChromiumLang)
            {
                list.Add(new KeyValuePair<string, string>("Chromium 参数", "--lang=" + language + "  --accept-lang=" + language));
            }
            if (config.StripSecrets)
            {
                list.Add(new KeyValuePair<string, string>("敏感环境变量", "已剥离"));
            }
            return list;
        }
    }
}

