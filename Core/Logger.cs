using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PrivacyGuard
{
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Event { get; set; }
        public string Detail { get; set; }
        public string Raw { get; set; }

        public LogEntry()
        {
            Level = "INFO";
            Source = "";
            Event = "";
            Detail = "";
            Raw = "";
        }

        public string TimeText
        {
            get { return Time.ToString("MM-dd HH:mm:ss"); }
        }

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case "ERROR": return "错误";
                    case "WARN": return "警告";
                    case "BLOCK": return "拦截";
                    default: return "信息";
                }
            }
        }

        public string DisplayEvent
        {
            get
            {
                switch (Event)
                {
                    case "START": return "服务启动";
                    case "STOP": return "服务停止";
                    case "APPLIED": return "应用策略";
                    case "REMOVED_ALL": return "移除防火墙规则";
                    case "RULE_ADDED_NOW": return "即时添加规则";
                    case "ADD_FAILED": return "防火墙规则失败";
                    case "APPLY_FAILED": return "防火墙应用失败";
                    case "REMOVE_ALL_FAILED": return "移除规则失败";
                    case "CREATE_FAILED": return "启动进程失败";
                    case "LAUNCHED": return "隐私启动";
                    case "BLOCKED_APP": return "检测/拦截应用";
                    case "PANIC_KILL": return "立即隔离";
                    case "PANIC_KILL_FAILED": return "隔离终止失败";
                    case "KILL_FAILED": return "终止进程失败";
                    case "RULE_REPAIR": return "修复防火墙规则";
                    case "POLICY_REPAIR": return "修复系统策略";
                    case "CONFIG": return "配置";
                    case "CREATED": return "创建默认配置";
                    case "LOAD_FAILED": return "配置读取失败";
                    case "RELOAD_FAILED": return "配置重载失败";
                    case "COMMAND": return "控制命令";
                    case "COMMAND_FAILED": return "控制命令失败";
                    case "INSTALLED": return "安装完成";
                    case "UNINSTALLED": return "卸载完成";
                    case "START_FAILED": return "界面启动失败";
                    case "TRAY_FAILED": return "托盘初始化失败";
                    case "CLIPBOARD_GUARD": return "剪贴板密钥守卫";
                    case "STATUS_WRITE_FAILED": return "状态写入失败";
                    case "ONSTART_FAILED": return "服务启动失败";
                    case "ONSTOP_FAILED": return "服务停止失败";
                    default: return Event;
                }
            }
        }
    }

    public static class Logger
    {
        private static readonly object Sync = new object();
        private static DateTime _lastCleanup = DateTime.MinValue;

        public static void Info(string source, string evt, string detail)
        {
            Write("INFO", source, evt, detail);
        }

        public static void Warn(string source, string evt, string detail)
        {
            Write("WARN", source, evt, detail);
        }

        public static void Error(string source, string evt, string detail)
        {
            Write("ERROR", source, evt, detail);
        }

        public static void Block(string source, string evt, string detail)
        {
            Write("BLOCK", source, evt, detail);
        }

        public static void Write(string level, string source, string evt, string detail)
        {
            try
            {
                lock (Sync)
                {
                    AppPaths.EnsureDataDir();
                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2} | {3} | {4}{5}",
                        DateTime.Now,
                        Clean(level),
                        Clean(source),
                        Clean(evt),
                        Clean(detail),
                        Environment.NewLine);
                    File.AppendAllText(AppPaths.LogFile("service"), line, new UTF8Encoding(false));

                    if ((DateTime.Now - _lastCleanup).TotalHours > 12)
                    {
                        _lastCleanup = DateTime.Now;
                        Cleanup();
                    }
                }
            }
            catch
            {
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string text = value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            if (text.Length > 4000) text = text.Substring(0, 4000) + "…";
            return text.Trim();
        }

        public static void Cleanup()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-30);
                foreach (string file in Directory.GetFiles(AppPaths.LogDir, "*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public static List<LogEntry> Tail(int maxEntries)
        {
            List<LogEntry> result = new List<LogEntry>();
            try
            {
                if (!Directory.Exists(AppPaths.LogDir)) return result;
                string[] files = Directory.GetFiles(AppPaths.LogDir, "*.log");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = files.Length - 1; i >= 0 && result.Count < maxEntries; i--)
                {
                    List<LogEntry> part = ReadFile(files[i], maxEntries - result.Count);
                    result.AddRange(part);
                }
            }
            catch
            {
            }
            return result;
        }

        private static List<LogEntry> ReadFile(string path, int maxEntries)
        {
            List<LogEntry> list = new List<LogEntry>();
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        LogEntry entry = Parse(line);
                        if (entry != null)
                        {
                            list.Add(entry);
                        }
                    }
                }
            }
            catch
            {
            }

            if (list.Count > maxEntries)
            {
                list = list.GetRange(list.Count - maxEntries, maxEntries);
            }
            list.Reverse();
            return list;
        }

        private static LogEntry Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] parts = line.Split(new string[] { " | " }, 5, StringSplitOptions.None);
            if (parts.Length < 5) return null;
            DateTime time;
            if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out time))
            {
                return null;
            }
            LogEntry entry = new LogEntry();
            entry.Time = time;
            entry.Level = parts[1];
            entry.Source = parts[2];
            entry.Event = parts[3];
            entry.Detail = parts[4];
            entry.Raw = line;
            return entry;
        }
    }

    public static class UiLog
    {
        private static readonly object Sync = new object();

        public static void Write(string level, string evt, string detail)
        {
            try
            {
                lock (Sync)
                {
                    AppPaths.EnsureDataDir();
                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | ui | {2} | {3}{4}",
                        DateTime.Now,
                        level,
                        evt.Replace("|", "/"),
                        (detail ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "/"),
                        Environment.NewLine);
                    File.AppendAllText(AppPaths.LogFile("ui"), line, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        public static void Info(string evt, string detail) { Write("INFO", evt, detail); }
        public static void Warn(string evt, string detail) { Write("WARN", evt, detail); }
        public static void Error(string evt, string detail) { Write("ERROR", evt, detail); }
    }
}
