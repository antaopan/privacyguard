using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PrivacyGuard
{
    public class ShieldVerificationReport
    {
        public string SystemTimeZone { get; set; }
        public string SystemCulture { get; set; }
        public string SystemUiCulture { get; set; }
        public string SystemLocaleName { get; set; }
        public string SystemUiLanguage { get; set; }
        public string ShieldTimeZone { get; set; }
        public string ShieldLanguage { get; set; }
        public string NodePath { get; set; }
        public string ChildTz { get; set; }
        public string ChildIntlTz { get; set; }
        public string ChildLang { get; set; }
        public string ChildLcAll { get; set; }
        public string ChildIntlLocale { get; set; }
        public bool NodeFound { get; set; }
        public bool TimeZonePass { get; set; }
        public bool EnvironmentPass { get; set; }
        public bool LanguageRuntimePass { get; set; }
        public string Error { get; set; }

        public ShieldVerificationReport()
        {
            SystemTimeZone = "";
            SystemCulture = "";
            SystemUiCulture = "";
            SystemLocaleName = "";
            SystemUiLanguage = "";
            ShieldTimeZone = "UTC";
            ShieldLanguage = "en-US";
            NodePath = "";
            ChildTz = "";
            ChildIntlTz = "";
            ChildLang = "";
            ChildLcAll = "";
            ChildIntlLocale = "";
            Error = "";
        }

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PrivacyGuard 时区 / 语言能力自检");
            sb.AppendLine("--------------------------------");
            sb.AppendLine("系统时区（.NET）：" + SystemTimeZone);
            sb.AppendLine("系统 Culture： " + SystemCulture + " / UI " + SystemUiCulture);
            sb.AppendLine("系统 UI 语言： " + SystemUiLanguage + "（" + SystemLocaleName + "）");
            sb.AppendLine("Shield 设置：  TZ=" + ShieldTimeZone + "，LANG=" + ShieldLanguage);
            sb.AppendLine();
            if (!NodeFound)
            {
                sb.AppendLine("未找到 node.exe，无法验证 Node/Electron 主进程的 TZ 行为。");
                if (!string.IsNullOrEmpty(Error)) sb.AppendLine("错误：" + Error);
                return sb.ToString();
            }

            sb.AppendLine("Node：        " + NodePath);
            sb.AppendLine("子进程 TZ：    " + ChildTz);
            sb.AppendLine("子进程 Intl 时区：" + ChildIntlTz);
            sb.AppendLine("子进程 LANG：   " + ChildLang);
            sb.AppendLine("子进程 LC_ALL： " + ChildLcAll);
            sb.AppendLine("子进程 Intl locale：" + ChildIntlLocale);
            sb.AppendLine();
            sb.AppendLine("时区修改： " + (TimeZonePass ? "PASS（Node / Electron 主进程）" : "FAIL"));
            sb.AppendLine("语言环境注入： " + (EnvironmentPass ? "PASS" : "FAIL"));
            sb.AppendLine("语言运行时切换： " + (LanguageRuntimePass ? "PASS" : "部分"));
            if (!LanguageRuntimePass)
            {
                sb.AppendLine("  说明：LANG 只改变环境变量，不会自动改变 Node/Electron 主进程的 ICU locale。");
                sb.AppendLine("  Electron 渲染进程的 navigator.language / Intl 需要 --lang；--lang 不改变");
                sb.AppendLine("  navigator.languages、app.getPreferredSystemLanguages() 或原生 Win32 语言 API。");
            }
            sb.AppendLine();
            sb.AppendLine("结论：时区通过 TZ 可验证地改变；语言只完成环境层注入，");
            sb.AppendLine("      完整语言隔离仍需独立的 Windows 用户 / 区域 / 虚拟机。");
            if (!string.IsNullOrEmpty(Error)) sb.AppendLine("错误：" + Error);
            return sb.ToString();
        }
    }

    public static class ShieldVerification
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort GetUserDefaultUILanguage();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);

        public static ShieldVerificationReport Verify(LauncherConfig config)
        {
            if (config == null) config = new LauncherConfig();
            ShieldVerificationReport report = new ShieldVerificationReport();
            report.SystemTimeZone = TimeZoneInfo.Local.Id;
            report.SystemCulture = CultureInfo.CurrentCulture.Name;
            report.SystemUiCulture = CultureInfo.CurrentUICulture.Name;
            report.SystemUiLanguage = Convert.ToString(GetUserDefaultUILanguage());
            try
            {
                StringBuilder locale = new StringBuilder(85);
                if (GetUserDefaultLocaleName(locale, locale.Capacity) > 0)
                {
                    report.SystemLocaleName = locale.ToString();
                }
            }
            catch
            {
            }
            report.ShieldTimeZone = string.IsNullOrEmpty(config.TimeZone) ? "UTC" : config.TimeZone.Trim();
            report.ShieldLanguage = string.IsNullOrEmpty(config.Language) ? "en-US" : config.Language.Trim();

            string node = FindNode();
            report.NodePath = node;
            report.NodeFound = !string.IsNullOrEmpty(node);
            if (!report.NodeFound)
            {
                report.Error = "PATH 中找不到 node.exe";
                return report;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "PrivacyGuardVerify-" + Guid.NewGuid().ToString("N"));
            string script = Path.Combine(tempDir, "probe.js");
            string output = Path.Combine(tempDir, "result.json");
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(script, @"
const fs = require('fs');
const dt = Intl.DateTimeFormat().resolvedOptions();
fs.writeFileSync(process.argv[2], JSON.stringify({
  tz: process.env.TZ || '',
  intlTz: dt.timeZone,
  lang: process.env.LANG || '',
  lcAll: process.env.LC_ALL || '',
  intlLocale: dt.locale
}));
", new UTF8Encoding(false));

                AppRule nodeRule = new AppRule();
                nodeRule.Name = "PrivacyGuard Runtime Verify";
                nodeRule.Path = node;
                nodeRule.Scope = Scopes.File;
                nodeRule.Network = NetworkModes.Off;
                nodeRule.Enabled = true;

                LauncherConfig verifyConfig = config.Clone();
                verifyConfig.StripSecrets = true;
                ShieldLaunchResult launch = ShieldLauncher.Launch(
                    nodeRule, verifyConfig,
                    "\"" + script + "\" \"" + output + "\"",
                    true);
                if (!launch.Success)
                {
                    report.Error = launch.Message;
                    return report;
                }

                for (int i = 0; i < 50 && !File.Exists(output); i++) Thread.Sleep(120);
                if (!File.Exists(output))
                {
                    report.Error = "Node 子进程没有在预期时间内写入结果";
                    return report;
                }

                string json = File.ReadAllText(output, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(json);
                report.ChildTz = Get(data, "tz");
                report.ChildIntlTz = Get(data, "intlTz");
                report.ChildLang = Get(data, "lang");
                report.ChildLcAll = Get(data, "lcAll");
                report.ChildIntlLocale = Get(data, "intlLocale");

                report.TimeZonePass = string.Equals(report.ChildTz, report.ShieldTimeZone, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(report.ChildIntlTz, report.ShieldTimeZone, StringComparison.OrdinalIgnoreCase);
                string expectedPosix = report.ShieldLanguage.Replace("-", "_") + ".UTF-8";
                report.EnvironmentPass = string.Equals(report.ChildLang, expectedPosix, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(report.ChildLcAll, expectedPosix, StringComparison.OrdinalIgnoreCase);
                report.LanguageRuntimePass = string.Equals(report.ChildIntlLocale, report.ShieldLanguage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(report.ChildIntlLocale, report.ShieldLanguage.Replace("-", "_"), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            return report;
        }

        private static string Get(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }

        private static string FindNode()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), "node.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch
                    {
                    }
                }
            }

            string[] common =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe")
            };
            for (int i = 0; i < common.Length; i++)
            {
                if (File.Exists(common[i])) return common[i];
            }
            return "";
        }
    }
}
