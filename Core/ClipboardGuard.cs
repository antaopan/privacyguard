using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace PrivacyGuard
{
    public class ClipboardGuard
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<List<AppRule>> _rules;
        private readonly Action<string> _alert;
        private DispatcherTimer _timer;
        private string _lastClearedHash = "";
        private DateTime _lastAlert = DateTime.MinValue;

        private static readonly Regex[] Patterns =
        {
            new Regex(@"sk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
            new Regex(@"sk-ant-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
            new Regex(@"gh[pousr]_[A-Za-z0-9]{20,}", RegexOptions.Compiled),
            new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),
            new Regex(@"xox[baprs]-[A-Za-z0-9\-]{10,}", RegexOptions.Compiled),
            new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled),
            new Regex(@"(?i)(api[_-]?key|secret|password|passwd|access[_-]?token|client[_-]?secret)\s*[:=]\s*\S{8,}", RegexOptions.Compiled),
            new Regex(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled)
        };

        public ClipboardGuard(Dispatcher dispatcher, Func<List<AppRule>> rules, Action<string> alert)
        {
            _dispatcher = dispatcher;
            _rules = rules;
            _alert = alert;
        }

        public bool Enabled { get; set; }

        public void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
            _timer.Interval = TimeSpan.FromMilliseconds(900);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public void Stop()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!Enabled) return;
            try
            {
                AppRule rule = ForegroundRule();
                if (rule == null) return;
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text) || text.Length < 12 || text.Length > 300000) return;
                if (!LooksSensitive(text)) return;

                string hash = Hash(text);
                if (string.Equals(hash, _lastClearedHash, StringComparison.Ordinal)) return;

                Clipboard.Clear();
                _lastClearedHash = hash;
                UiLog.Warn("CLIPBOARD_GUARD", "已清除剪贴板中的疑似密钥 · 前台应用=" + rule.Name);
                if ((DateTime.Now - _lastAlert).TotalSeconds > 5)
                {
                    _lastAlert = DateTime.Now;
                    if (_alert != null)
                    {
                        _alert("已阻止将疑似密钥/令牌粘贴到 " + rule.Name + "，剪贴板已清除");
                    }
                }
            }
            catch
            {
            }
        }

        private AppRule ForegroundRule()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                int pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid <= 0 || pid == Process.GetCurrentProcess().Id) return null;
                string path;
                using (Process process = Process.GetProcessById(pid))
                {
                    path = process.MainModule.FileName;
                }
                if (string.IsNullOrEmpty(path)) return null;
                path = AppPaths.CleanPath(path);
                List<AppRule> rules = _rules == null ? null : _rules();
                if (rules == null) return null;
                for (int i = 0; i < rules.Count; i++)
                {
                    AppRule rule = rules[i];
                    if (rule != null && rule.Enabled && AppMatcher.Matches(rule, path)) return rule;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool LooksSensitive(string text)
        {
            for (int i = 0; i < Patterns.Length; i++)
            {
                if (Patterns[i].IsMatch(text)) return true;
            }
            return false;
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
