using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace PrivacyGuard
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private AppConfig _config;
        private bool _protectionEnabled;
        private bool _serviceOnline;
        private bool _serviceInstalled;
        private int _ruleCount;
        private int _blockedToday;
        private int _hardeningApplied;
        private bool _firewallEnabled;
        private string _firewallDetail;
        private string _lastError;
        private string _lastEvent;
        private string _lastSyncText;
        private string _pageTitle;
        private string _pageSubtitle;
        private string _riskMessage;
        private string _appliedAtText;
        private string _uptimeText;
        private string _version;
        private bool _isBusy;
        private string _busyText;

        public ObservableCollection<AppItem> Apps { get; private set; }
        public ObservableCollection<HardeningItem> Hardening { get; private set; }
        public ObservableCollection<LogEntry> Logs { get; private set; }
        public ObservableCollection<LogEntry> RecentLogs { get; private set; }
        public ObservableCollection<ChecklistItem> Checklist { get; private set; }

        public LauncherConfig Launcher { get; private set; }
        public UiConfig Ui { get; private set; }
        public AppConfig Config { get { return _config; } }

        public MainViewModel()
        {
            Apps = new ObservableCollection<AppItem>();
            Hardening = new ObservableCollection<HardeningItem>();
            Logs = new ObservableCollection<LogEntry>();
            RecentLogs = new ObservableCollection<LogEntry>();
            Checklist = new ObservableCollection<ChecklistItem>();
            Launcher = new LauncherConfig();
            Ui = new UiConfig();
            _pageTitle = "总览";
            _pageSubtitle = "本机 AI 隐私防护状态";
            _lastSyncText = "";
            _riskMessage = "";
            _firewallDetail = "";
            _lastError = "";
            _lastEvent = "";
            _appliedAtText = "等待策略应用";
            _uptimeText = "";
            _version = ProductInfo.Version;
            _busyText = "";
        }

        public void Load(AppConfig config)
        {
            _config = config ?? new AppConfig();
            _config.Normalize();

            Apps.Clear();
            if (_config.Apps != null)
            {
                foreach (AppRule rule in _config.Apps)
                {
                    if (rule != null) Apps.Add(AppItem.FromRule(rule));
                }
            }

            Hardening.Clear();
            foreach (PolicyDefinition def in PolicyManager.GetCatalog())
            {
                HardeningItem item = new HardeningItem();
                item.Id = def.Id;
                item.Title = def.Title;
                item.Description = def.Description;
                item.Recommended = def.Recommended;
                item.NeedsSignOut = def.NeedsSignOut;
                item.IsEnabled = _config.Hardening != null && _config.Hardening.Get(def.Id);
                item.IconGlyph = GlyphFor(def.Id);
                item.WarningText = WarningFor(def.Id);
                Hardening.Add(item);
            }

            Launcher = _config.Launcher == null ? new LauncherConfig() : _config.Launcher.Clone();
            Ui = _config.Ui == null ? new UiConfig() : _config.Ui.Clone();
            ProtectionEnabled = _config.Enabled;

            Raise("Launcher");
            Raise("Ui");
            Raise("AppCountText");
            Raise("EnabledAppCountText");
            Raise("HasApps");
            Raise("HardeningEnabledCount");
            Raise("HardeningTotal");
            Raise("ShieldSummary");
            Raise("LauncherPreview");
            UpdateRisk();
        }

        public AppConfig BuildConfig()
        {
            AppConfig cfg = _config == null ? new AppConfig() : _config.Clone();
            cfg.Enabled = ProtectionEnabled;
            cfg.Apps = new List<AppRule>();
            foreach (AppItem item in Apps)
            {
                if (item != null) cfg.Apps.Add(item.ToRule());
            }
            if (cfg.Hardening == null) cfg.Hardening = new HardeningConfig();
            foreach (HardeningItem item in Hardening)
            {
                cfg.Hardening.Set(item.Id, item.IsEnabled);
            }
            cfg.Launcher = Launcher == null ? new LauncherConfig() : Launcher.Clone();
            cfg.Ui = Ui == null ? new UiConfig() : Ui.Clone();
            _config = cfg;
            return cfg;
        }

        public void RefreshRisk()
        {
            UpdateRisk();
        }

        public void NotifyHardeningChanged()
        {
            Raise("HardeningEnabledCount");
            UpdateRisk();
            UpdateChecklist();
        }

        public bool ContainsPath(string path)
        {
            string normalized = AppPaths.CleanPath(path);
            return Apps.Any(a => string.Equals(AppPaths.CleanPath(a.Path), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public void AddApp(AppRule rule)
        {
            if (rule == null) return;
            AppRule existing = Apps.Select(a => a.ToRule()).FirstOrDefault(a => AppMatcher.Matches(a, rule.Path));
            if (existing != null) return;
            Apps.Add(AppItem.FromRule(rule));
            Raise("AppCountText");
            Raise("EnabledAppCountText");
            Raise("HasApps");
            UpdateRisk();
        }

        public void RemoveApp(AppItem item)
        {
            if (item == null) return;
            Apps.Remove(item);
            Raise("AppCountText");
            Raise("EnabledAppCountText");
            Raise("HasApps");
            UpdateRisk();
        }

        public void ApplyRecommendedHardening()
        {
            foreach (HardeningItem item in Hardening)
            {
                PolicyDefinition def = PolicyManager.Find(item.Id);
                if (def != null && def.Recommended) item.IsEnabled = true;
            }
            Raise("HardeningEnabledCount");
        }

        public bool ProtectionEnabled
        {
            get { return _protectionEnabled; }
            set
            {
                if (_protectionEnabled == value) return;
                _protectionEnabled = value;
                Raise("ProtectionEnabled");
                Raise("StatusTitle");
                Raise("StatusSubtitle");
                Raise("MasterStateText");
            }
        }

        public bool ServiceOnline
        {
            get { return _serviceOnline; }
            set
            {
                if (_serviceOnline == value) return;
                _serviceOnline = value;
                Raise("ServiceOnline");
                Raise("StatusTitle");
                Raise("StatusSubtitle");
                Raise("ServiceStateText");
                Raise("ServiceStateBrush");
                Raise("MasterStateText");
                Raise("HasServiceProblem");
                UpdateRisk();
            }
        }

        public bool ServiceInstalled
        {
            get { return _serviceInstalled; }
            set
            {
                if (_serviceInstalled == value) return;
                _serviceInstalled = value;
                Raise("ServiceInstalled");
                Raise("ServiceStateText");
                Raise("HasServiceProblem");
                UpdateRisk();
            }
        }

        public int RuleCount
        {
            get { return _ruleCount; }
            set { if (_ruleCount != value) { _ruleCount = value; Raise("RuleCount"); } }
        }

        public int BlockedToday
        {
            get { return _blockedToday; }
            set { if (_blockedToday != value) { _blockedToday = value; Raise("BlockedToday"); } }
        }

        public int HardeningApplied
        {
            get { return _hardeningApplied; }
            set { if (_hardeningApplied != value) { _hardeningApplied = value; Raise("HardeningApplied"); } }
        }

        public bool FirewallEnabled
        {
            get { return _firewallEnabled; }
            set
            {
                if (_firewallEnabled == value) return;
                _firewallEnabled = value;
                Raise("FirewallEnabled");
                Raise("FirewallStateText");
                UpdateRisk();
            }
        }

        public string FirewallDetail
        {
            get { return _firewallDetail; }
            set { if (_firewallDetail != value) { _firewallDetail = value; Raise("FirewallDetail"); } }
        }

        public string LastError
        {
            get { return _lastError; }
            set
            {
                if (_lastError == value) return;
                _lastError = value;
                Raise("LastError");
                Raise("HasLastError");
                UpdateRisk();
            }
        }

        public string LastEvent
        {
            get { return _lastEvent; }
            set { if (_lastEvent != value) { _lastEvent = value; Raise("LastEvent"); } }
        }

        public string ServiceStateText
        {
            get
            {
                if (!_serviceInstalled) return "未安装";
                if (_serviceOnline) return "运行中";
                return "已停止";
            }
        }

        public Brush ServiceStateBrush
        {
            get
            {
                if (_serviceOnline) return BrushFrom("#34D399");
                if (_serviceInstalled) return BrushFrom("#FBBF24");
                return BrushFrom("#FB7185");
            }
        }

        public string LastSyncText
        {
            get { return _lastSyncText; }
            set { if (_lastSyncText != value) { _lastSyncText = value; Raise("LastSyncText"); } }
        }

        public string StatusTitle
        {
            get
            {
                if (!_serviceOnline) return "后台防护未运行";
                return _protectionEnabled ? "保护已开启" : "保护已暂停";
            }
        }

        public string StatusSubtitle
        {
            get
            {
                if (!_serviceOnline) return "安装或启动后台服务后，网络隔离与系统加固才会生效";
                if (!_protectionEnabled) return "暂停期间，黑名单应用可以联网，系统加固也已临时恢复";
                return "后台服务运行中 · 网络隔离与 AI 隐私加固已生效";
            }
        }

        public string MasterStateText
        {
            get
            {
                if (!_serviceOnline) return "等待服务";
                return _protectionEnabled ? "已启用" : "已暂停";
            }
        }

        public string AppCountText
        {
            get { return Apps.Count.ToString(); }
        }

        public bool HasApps
        {
            get { return Apps.Count > 0; }
        }

        public string EnabledAppCountText
        {
            get { return Apps.Count(a => a.Enabled).ToString(); }
        }

        public int HardeningEnabledCount
        {
            get { return Hardening.Count(h => h.IsEnabled); }
        }

        public int HardeningTotal
        {
            get { return Hardening.Count; }
        }

        public string ShieldSummary
        {
            get
            {
                if (Launcher == null) return "UTC · en-US";
                return Launcher.TimeZone + " · " + Launcher.Language +
                       (Launcher.StripSecrets ? " · 密钥剥离" : "");
            }
        }

        public List<KeyValuePair<string, string>> LauncherPreview
        {
            get { return ShieldLauncher.Preview(Launcher); }
        }

        public string PageTitle
        {
            get { return _pageTitle; }
            set { if (_pageTitle != value) { _pageTitle = value; Raise("PageTitle"); } }
        }

        public string PageSubtitle
        {
            get { return _pageSubtitle; }
            set { if (_pageSubtitle != value) { _pageSubtitle = value; Raise("PageSubtitle"); } }
        }

        public string AppliedAtText
        {
            get { return _appliedAtText; }
            set { if (_appliedAtText != value) { _appliedAtText = value; Raise("AppliedAtText"); } }
        }

        public string UptimeText
        {
            get { return _uptimeText; }
            set { if (_uptimeText != value) { _uptimeText = value; Raise("UptimeText"); } }
        }

        public string Version
        {
            get { return _version; }
            set { if (_version != value) { _version = value; Raise("Version"); } }
        }

        public string DataDir
        {
            get { return AppPaths.DataDir; }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { if (_isBusy != value) { _isBusy = value; Raise("IsBusy"); } }
        }

        public string BusyText
        {
            get { return _busyText; }
            set { if (_busyText != value) { _busyText = value; Raise("BusyText"); } }
        }

        public string RiskMessage
        {
            get { return _riskMessage; }
            private set
            {
                if (_riskMessage == value) return;
                _riskMessage = value;
                Raise("RiskMessage");
                Raise("HasRisk");
            }
        }

        public bool HasRisk
        {
            get { return !string.IsNullOrEmpty(_riskMessage); }
        }

        public bool HasLastError
        {
            get { return !string.IsNullOrEmpty(_lastError); }
        }

        public bool HasServiceProblem
        {
            get { return !_serviceOnline; }
        }

        public string FirewallStateText
        {
            get { return _firewallEnabled ? "已启用" : "未启用"; }
        }

        public void SetPage(int index)
        {
            switch (index)
            {
                case 0:
                    PageTitle = "总览";
                    PageSubtitle = "本机 AI 隐私防护状态";
                    break;
                case 1:
                    PageTitle = "应用黑名单";
                    PageSubtitle = "管理哪些程序会被网络隔离与进程监控";
                    break;
                case 2:
                    PageTitle = "隐私启动器";
                    PageSubtitle = "以 UTC / en-US 与干净环境变量启动 AI 应用";
                    break;
                case 3:
                    PageTitle = "系统加固";
                    PageSubtitle = "关闭 Windows 中面向 AI 的系统级数据采集";
                    break;
                case 4:
                    PageTitle = "防护日志";
                    PageSubtitle = "查看拦截、启动与服务事件";
                    break;
                case 5:
                    PageTitle = "设置";
                    PageSubtitle = "服务、自启动与关于";
                    break;
            }
        }

        public void UpdateStatus(ServiceStatus status, bool online, bool installed, bool updateProtection)
        {
            ServiceInstalled = installed;
            ServiceOnline = online;
            if (status == null)
            {
                RuleCount = 0;
                BlockedToday = 0;
                HardeningApplied = 0;
                FirewallEnabled = false;
                FirewallDetail = "无状态";
                LastError = installed ? "后台服务未响应" : "后台服务尚未安装";
                LastSyncText = "未同步";
                UpdateRisk();
                return;
            }

            if (updateProtection) ProtectionEnabled = status.ProtectionEnabled && online;
            RuleCount = status.RuleCount;
            BlockedToday = status.BlockedToday;
            HardeningApplied = status.HardeningApplied;
            FirewallEnabled = status.FirewallEnabled;
            FirewallDetail = status.FirewallDetail;
            LastError = status.LastError;
            LastEvent = status.LastEvent;
            AppliedAtText = string.IsNullOrEmpty(status.AppliedAt) ? "等待策略应用" : "上次应用 " + FormatTime(status.AppliedAt);
            UptimeText = FormatUptime(status.UptimeSeconds);
            Version = string.IsNullOrEmpty(status.Version) ? ProductInfo.Version : status.Version;
            LastSyncText = online ? "刚刚同步" : "状态已过期";
            UpdateChecklist();
            UpdateRisk();
        }

        public void RefreshLogs(List<LogEntry> all, string level, string query)
        {
            Logs.Clear();
            RecentLogs.Clear();
            if (all != null)
            {
                for (int i = 0; i < all.Count && i < 5; i++) RecentLogs.Add(all[i]);
            }
            if (all == null) return;
            string q = (query ?? "").Trim();
            int count = 0;
            foreach (LogEntry entry in all)
            {
                if (!string.IsNullOrEmpty(level) && level != "全部" && entry.Level != LevelCode(level)) continue;
                if (q.Length > 0 &&
                    (entry.Event ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (entry.Detail ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (entry.Source ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                Logs.Add(entry);
                if (++count >= 500) break;
            }
        }

        private static string LevelCode(string label)
        {
            switch (label)
            {
                case "拦截": return "BLOCK";
                case "警告": return "WARN";
                case "错误": return "ERROR";
                case "信息": return "INFO";
                default: return label;
            }
        }

        public void UpdateChecklist()
        {
            Checklist.Clear();
            Checklist.Add(Item("后台服务", _serviceOnline ? "运行中" : "未运行", _serviceOnline, "\uE73E"));
            Checklist.Add(Item("Windows 防火墙", _firewallEnabled ? "已启用" : "未启用", _firewallEnabled, "\uE83D"));
            Checklist.Add(Item("黑名单应用", Apps.Count == 0 ? "尚未添加" : Apps.Count + " 个应用", Apps.Count > 0, "\uE71D"));
            Checklist.Add(Item("网络隔离规则", _ruleCount == 0 ? "暂无规则" : _ruleCount + " 条规则", _ruleCount > 0, "\uE968"));
            Checklist.Add(Item("AI 数据采集", IsHardeningOn("recall") ? "已关闭" : "仍可能开启", IsHardeningOn("recall"), "\uE99A"));
            Checklist.Add(Item("指纹伪装", Launcher != null ? "UTC / en-US" : "未配置", Launcher != null, "\uE8C1"));
        }

        private bool IsHardeningOn(string id)
        {
            HardeningItem item = Hardening.FirstOrDefault(h => h.Id == id);
            return item != null && item.IsEnabled;
        }

        private static ChecklistItem Item(string title, string detail, bool ok, string glyph)
        {
            ChecklistItem item = new ChecklistItem();
            item.Title = title;
            item.Detail = detail;
            item.State = ok ? "ok" : "warn";
            item.Glyph = ok ? "\uE73E" : "\uE7BA";
            item.StateBrush = BrushFrom(ok ? "#34D399" : "#FBBF24");
            return item;
        }

        private void UpdateRisk()
        {
            if (!_serviceInstalled)
            {
                RiskMessage = "后台服务尚未安装：当前只有界面，网络隔离不会生效。请点击“安装 / 修复后台服务”。";
            }
            else if (!_serviceOnline)
            {
                RiskMessage = "后台服务未运行：黑名单应用当前不受网络隔离保护。请启动或修复服务。";
            }
            else if (!_firewallEnabled)
            {
                RiskMessage = "Windows 防火墙未启用：程序规则无法生效。请在 Windows 安全中心启用防火墙。";
            }
            else if (Apps.Count == 0)
            {
                RiskMessage = "黑名单为空：先添加需要隔离的 AI 应用，防护才会真正开始。";
            }
            else if (!string.IsNullOrEmpty(_lastError))
            {
                RiskMessage = "最近一次应用策略时出现问题：" + _lastError;
            }
            else if (!IsHardeningOn("recall"))
            {
                RiskMessage = "Windows Recall / AI 数据采集尚未关闭，系统仍可能记录屏幕内容。";
            }
            else
            {
                RiskMessage = "";
            }
        }

        private static string GlyphFor(string id)
        {
            switch (id)
            {
                case "recall": return "\uE7B3";
                case "copilot": return "\uE99A";
                case "clipboard": return "\uE77F";
                case "advertising": return "\uE8D4";
                case "activity": return "\uE81C";
                case "cloudsearch": return "\uE721";
                case "telemetry": return "\uE9D2";
                case "location": return "\uE707";
                case "cameramic": return "\uE714";
                default: return "\uE72E";
            }
        }

        private static string WarningFor(string id)
        {
            switch (id)
            {
                case "location": return "会影响地图、天气和查找设备";
                case "cameramic": return "会影响会议、通话和录像";
                case "telemetry": return "可能影响企业支持与更新诊断";
                default: return "";
            }
        }

        private static string FormatTime(string iso)
        {
            DateTime time;
            if (DateTime.TryParse(iso, out time))
            {
                return time.ToLocalTime().ToString("HH:mm:ss");
            }
            return "—";
        }

        private static string FormatUptime(long seconds)
        {
            if (seconds <= 0) return "—";
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            if (span.TotalDays >= 1) return string.Format("{0} 天 {1} 小时", (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1) return string.Format("{0} 小时 {1} 分", (int)span.TotalHours, span.Minutes);
            return string.Format("{0} 分 {1} 秒", span.Minutes, span.Seconds);
        }

        private static Brush BrushFrom(string color)
        {
            SolidColorBrush brush = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        public void NotifyLauncherChanged()
        {
            Raise("ShieldSummary");
            Raise("LauncherPreview");
            UpdateChecklist();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}

