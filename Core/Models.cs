using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace PrivacyGuard
{
    public static class ProductInfo
    {
        public const string Name = "PrivacyGuard";
        public const string DisplayName = "隐私卫士";
        public const string Version = "1.1.1";
        public const string ServiceName = "PrivacyGuard";
        public const string TaskName = "PrivacyGuard";
        public const string FirewallGroup = "PrivacyGuard";
        public const string PipeName = "PrivacyGuard.Control.v1";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PrivacyGuard";
    }

    public static class NetworkModes
    {
        public const string Full = "full";
        public const string Lan = "lan";
        public const string Inbound = "inbound";
        public const string Off = "off";

        public static bool IsValid(string value)
        {
            return value == Full || value == Lan || value == Inbound || value == Off;
        }

        public static string Label(string value)
        {
            switch (value)
            {
                case Full: return "完全断网";
                case Lan: return "仅阻止局域网";
                case Inbound: return "仅阻止入站";
                case Off: return "不拦截";
                default: return "未知";
            }
        }

        public static string Hint(string value)
        {
            switch (value)
            {
                case Full: return "阻断全部入站与出站流量，云端 AI 将无法使用";
                case Lan: return "允许访问互联网，阻断本机所在局域网";
                case Inbound: return "允许应用联网，但禁止外部通过本机 IP 连入";
                case Off: return "不做网络拦截，仅保留启动器与进程监控";
                default: return "";
            }
        }
    }

    public static class Scopes
    {
        public const string File = "file";
        public const string Folder = "folder";

        public static bool IsValid(string value)
        {
            return value == File || value == Folder;
        }

        public static string Label(string value)
        {
            return value == Folder ? "整个安装目录" : "仅此程序";
        }
    }

    [DataContract]
    public class AppRule
    {
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        [DataMember(Name = "path", Order = 3)]
        public string Path { get; set; }

        [DataMember(Name = "root", Order = 4)]
        public string Root { get; set; }

        [DataMember(Name = "scope", Order = 5)]
        public string Scope { get; set; }

        [DataMember(Name = "network", Order = 6)]
        public string Network { get; set; }

        [DataMember(Name = "shield", Order = 7)]
        public bool Shield { get; set; }

        [DataMember(Name = "kill", Order = 8)]
        public bool Kill { get; set; }

        [DataMember(Name = "enabled", Order = 9)]
        public bool Enabled { get; set; }

        public AppRule()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Path = "";
            Root = "";
            Scope = Scopes.File;
            Network = NetworkModes.Full;
            Shield = true;
            Kill = false;
            Enabled = true;
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }

            Path = AppPaths.CleanPath(Path);
            Root = AppPaths.CleanPath(Root);

            if (!Scopes.IsValid(Scope))
            {
                Scope = Scopes.File;
            }

            if (Scope == Scopes.Folder)
            {
                if (string.IsNullOrEmpty(Root))
                {
                    Root = AppMatcher.InferRoot(Path);
                }
            }
            else
            {
                Root = "";
            }

            if (!NetworkModes.IsValid(Network))
            {
                Network = NetworkModes.Full;
            }

            if (string.IsNullOrEmpty(Name))
            {
                try
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(Path);
                }
                catch
                {
                    Name = "未命名应用";
                }
            }
        }

        public AppRule Clone()
        {
            AppRule copy = (AppRule)MemberwiseClone();
            return copy;
        }

        public override string ToString()
        {
            return Name + " (" + Path + ")";
        }
    }

    [DataContract]
    public class HardeningConfig
    {
        [DataMember(Name = "recall", Order = 1)]
        public bool DisableRecall { get; set; }

        [DataMember(Name = "copilot", Order = 2)]
        public bool DisableCopilot { get; set; }

        [DataMember(Name = "clipboard", Order = 3)]
        public bool DisableClipboardCloud { get; set; }

        [DataMember(Name = "advertising", Order = 4)]
        public bool DisableAdvertisingId { get; set; }

        [DataMember(Name = "activity", Order = 5)]
        public bool DisableActivityHistory { get; set; }

        [DataMember(Name = "telemetry", Order = 6)]
        public bool DisableTelemetry { get; set; }

        [DataMember(Name = "cloudsearch", Order = 7)]
        public bool DisableCloudSearch { get; set; }

        [DataMember(Name = "location", Order = 8)]
        public bool DisableLocation { get; set; }

        [DataMember(Name = "cameramic", Order = 9)]
        public bool DisableCameraMic { get; set; }

        public HardeningConfig()
        {
            SetRecommended();
        }

        public void SetRecommended()
        {
            DisableRecall = true;
            DisableCopilot = true;
            DisableClipboardCloud = true;
            DisableAdvertisingId = true;
            DisableActivityHistory = true;
            DisableTelemetry = false;
            DisableCloudSearch = true;
            DisableLocation = false;
            DisableCameraMic = false;
        }

        public bool Get(string id)
        {
            switch (id)
            {
                case "recall": return DisableRecall;
                case "copilot": return DisableCopilot;
                case "clipboard": return DisableClipboardCloud;
                case "advertising": return DisableAdvertisingId;
                case "activity": return DisableActivityHistory;
                case "telemetry": return DisableTelemetry;
                case "cloudsearch": return DisableCloudSearch;
                case "location": return DisableLocation;
                case "cameramic": return DisableCameraMic;
                default: return false;
            }
        }

        public void Set(string id, bool value)
        {
            switch (id)
            {
                case "recall": DisableRecall = value; break;
                case "copilot": DisableCopilot = value; break;
                case "clipboard": DisableClipboardCloud = value; break;
                case "advertising": DisableAdvertisingId = value; break;
                case "activity": DisableActivityHistory = value; break;
                case "telemetry": DisableTelemetry = value; break;
                case "cloudsearch": DisableCloudSearch = value; break;
                case "location": DisableLocation = value; break;
                case "cameramic": DisableCameraMic = value; break;
            }
        }

        public int EnabledCount()
        {
            int n = 0;
            if (DisableRecall) n++;
            if (DisableCopilot) n++;
            if (DisableClipboardCloud) n++;
            if (DisableAdvertisingId) n++;
            if (DisableActivityHistory) n++;
            if (DisableTelemetry) n++;
            if (DisableCloudSearch) n++;
            if (DisableLocation) n++;
            if (DisableCameraMic) n++;
            return n;
        }

        public HardeningConfig Clone()
        {
            return (HardeningConfig)MemberwiseClone();
        }
    }

    [DataContract]
    public class LauncherConfig
    {
        [DataMember(Name = "timezone", Order = 1)]
        public string TimeZone { get; set; }

        [DataMember(Name = "language", Order = 2)]
        public string Language { get; set; }

        [DataMember(Name = "stripSecrets", Order = 3)]
        public bool StripSecrets { get; set; }

        [DataMember(Name = "neutralWorkingDir", Order = 4)]
        public bool NeutralWorkingDir { get; set; }

        [DataMember(Name = "chromiumLang", Order = 5)]
        public bool ChromiumLang { get; set; }

        [DataMember(Name = "fakeComputerName", Order = 6)]
        public string FakeComputerName { get; set; }

        [DataMember(Name = "extraArgs", Order = 7)]
        public string ExtraArgs { get; set; }

        public LauncherConfig()
        {
            TimeZone = "UTC";
            Language = "en-US";
            StripSecrets = true;
            NeutralWorkingDir = true;
            ChromiumLang = true;
            FakeComputerName = "";
            ExtraArgs = "";
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(TimeZone)) TimeZone = "UTC";
            if (string.IsNullOrEmpty(Language)) Language = "en-US";
            if (FakeComputerName == null) FakeComputerName = "";
            if (ExtraArgs == null) ExtraArgs = "";
        }

        public LauncherConfig Clone()
        {
            return (LauncherConfig)MemberwiseClone();
        }
    }

    [DataContract]
    public class UiConfig
    {
        [DataMember(Name = "clipboardGuard", Order = 1)]
        public bool ClipboardGuard { get; set; }

        [DataMember(Name = "closeToTray", Order = 2)]
        public bool CloseToTray { get; set; }

        [DataMember(Name = "notifyOnBlock", Order = 3)]
        public bool NotifyOnBlock { get; set; }

        [DataMember(Name = "firstRunDone", Order = 4)]
        public bool FirstRunDone { get; set; }

        public UiConfig()
        {
            ClipboardGuard = true;
            CloseToTray = true;
            NotifyOnBlock = true;
            FirstRunDone = false;
        }

        public UiConfig Clone()
        {
            return (UiConfig)MemberwiseClone();
        }
    }

    [DataContract]
    public class AppConfig
    {
        [DataMember(Name = "version", Order = 1)]
        public int Version { get; set; }

        [DataMember(Name = "enabled", Order = 2)]
        public bool Enabled { get; set; }

        [DataMember(Name = "apps", Order = 3)]
        public List<AppRule> Apps { get; set; }

        [DataMember(Name = "hardening", Order = 4)]
        public HardeningConfig Hardening { get; set; }

        [DataMember(Name = "launcher", Order = 5)]
        public LauncherConfig Launcher { get; set; }

        [DataMember(Name = "ui", Order = 6)]
        public UiConfig Ui { get; set; }

        [DataMember(Name = "updatedAt", Order = 7)]
        public string UpdatedAt { get; set; }

        [DataMember(Name = "revision", Order = 8)]
        public long Revision { get; set; }

        [DataMember(Name = "autoAdded", Order = 9)]
        public List<string> AutoAdded { get; set; }

        public AppConfig()
        {
            Version = 1;
            Enabled = true;
            Apps = new List<AppRule>();
            AutoAdded = new List<string>();
            Hardening = new HardeningConfig();
            Launcher = new LauncherConfig();
            Ui = new UiConfig();
            UpdatedAt = DateTime.UtcNow.ToString("o");
            Revision = DateTime.UtcNow.Ticks;
        }

        public void Normalize()
        {
            if (Version <= 0) Version = 1;
            if (Apps == null) Apps = new List<AppRule>();
            if (AutoAdded == null) AutoAdded = new List<string>();
            if (Hardening == null) Hardening = new HardeningConfig();
            if (Launcher == null) Launcher = new LauncherConfig();
            if (Ui == null) Ui = new UiConfig();
            if (string.IsNullOrEmpty(UpdatedAt)) UpdatedAt = DateTime.UtcNow.ToString("o");
            if (Revision <= 0) Revision = DateTime.UtcNow.Ticks;

            for (int i = 0; i < Apps.Count; i++)
            {
                if (Apps[i] == null)
                {
                    Apps.RemoveAt(i);
                    i--;
                    continue;
                }
                Apps[i].Normalize();
            }

            Launcher.Normalize();
        }

        public AppConfig Clone()
        {
            AppConfig copy = new AppConfig();
            copy.Version = Version;
            copy.Enabled = Enabled;
            copy.Apps = new List<AppRule>();
            if (Apps != null)
            {
                for (int i = 0; i < Apps.Count; i++)
                {
                    if (Apps[i] != null) copy.Apps.Add(Apps[i].Clone());
                }
            }
            copy.AutoAdded = AutoAdded == null ? new List<string>() : new List<string>(AutoAdded);
            copy.Hardening = Hardening == null ? new HardeningConfig() : Hardening.Clone();
            copy.Launcher = Launcher == null ? new LauncherConfig() : Launcher.Clone();
            copy.Ui = Ui == null ? new UiConfig() : Ui.Clone();
            copy.UpdatedAt = UpdatedAt;
            copy.Revision = Revision;
            return copy;
        }

        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow.ToString("o");
            Revision = DateTime.UtcNow.Ticks;
        }

        public AppRule FindByPath(string path)
        {
            if (Apps == null) return null;
            string normalized = AppPaths.CleanPath(path);
            for (int i = 0; i < Apps.Count; i++)
            {
                AppRule rule = Apps[i];
                if (rule == null) continue;
                if (AppMatcher.Matches(rule, normalized)) return rule;
            }
            return null;
        }

        public HardeningConfig HardeningOrDefault
        {
            get { return Hardening ?? new HardeningConfig(); }
        }
    }

    [DataContract]
    public class ServiceStatus
    {
        [DataMember(Name = "timestamp", Order = 1)]
        public string Timestamp { get; set; }

        [DataMember(Name = "serviceRunning", Order = 2)]
        public bool ServiceRunning { get; set; }

        [DataMember(Name = "protectionEnabled", Order = 3)]
        public bool ProtectionEnabled { get; set; }

        [DataMember(Name = "serviceStartedAt", Order = 4)]
        public string ServiceStartedAt { get; set; }

        [DataMember(Name = "appliedAt", Order = 5)]
        public string AppliedAt { get; set; }

        [DataMember(Name = "configRevision", Order = 6)]
        public long ConfigRevision { get; set; }

        [DataMember(Name = "appCount", Order = 7)]
        public int AppCount { get; set; }

        [DataMember(Name = "enabledAppCount", Order = 8)]
        public int EnabledAppCount { get; set; }

        [DataMember(Name = "ruleCount", Order = 9)]
        public int RuleCount { get; set; }

        [DataMember(Name = "blockedToday", Order = 10)]
        public int BlockedToday { get; set; }

        [DataMember(Name = "blockedTotal", Order = 11)]
        public int BlockedTotal { get; set; }

        [DataMember(Name = "firewallEnabled", Order = 12)]
        public bool FirewallEnabled { get; set; }

        [DataMember(Name = "firewallDetail", Order = 13)]
        public string FirewallDetail { get; set; }

        [DataMember(Name = "hardeningApplied", Order = 14)]
        public int HardeningApplied { get; set; }

        [DataMember(Name = "lastError", Order = 15)]
        public string LastError { get; set; }

        [DataMember(Name = "lastEvent", Order = 16)]
        public string LastEvent { get; set; }

        [DataMember(Name = "lastBlockedName", Order = 17)]
        public string LastBlockedName { get; set; }

        [DataMember(Name = "lastBlockedAt", Order = 18)]
        public string LastBlockedAt { get; set; }

        [DataMember(Name = "lastCommandId", Order = 19)]
        public string LastCommandId { get; set; }

        [DataMember(Name = "version", Order = 20)]
        public string Version { get; set; }

        [DataMember(Name = "uptimeSeconds", Order = 21)]
        public long UptimeSeconds { get; set; }

        public ServiceStatus()
        {
            Timestamp = DateTime.UtcNow.ToString("o");
            Version = ProductInfo.Version;
            FirewallDetail = "";
            LastError = "";
            LastEvent = "";
            LastBlockedName = "";
            LastBlockedAt = "";
            LastCommandId = "";
            AppliedAt = "";
            ServiceStartedAt = "";
        }

        public static ServiceStatus Offline()
        {
            ServiceStatus s = new ServiceStatus();
            s.ServiceRunning = false;
            s.ProtectionEnabled = false;
            s.FirewallDetail = "后台服务未运行";
            s.LastError = "后台服务未运行或尚未安装";
            return s;
        }
    }

    [DataContract]
    public class ControlCommand
    {
        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "command", Order = 2)]
        public string Command { get; set; }

        [DataMember(Name = "createdAt", Order = 3)]
        public string CreatedAt { get; set; }

        public ControlCommand()
        {
            Id = Guid.NewGuid().ToString("N");
            Command = "apply";
            CreatedAt = DateTime.UtcNow.ToString("o");
        }
    }

    [DataContract]
    public class StatsData
    {
        [DataMember(Name = "date", Order = 1)]
        public string Date { get; set; }

        [DataMember(Name = "blockedToday", Order = 2)]
        public int BlockedToday { get; set; }

        [DataMember(Name = "blockedTotal", Order = 3)]
        public int BlockedTotal { get; set; }

        public StatsData()
        {
            Date = DateTime.Now.ToString("yyyy-MM-dd");
            BlockedToday = 0;
            BlockedTotal = 0;
        }

        public void RollIfNeeded()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (Date != today)
            {
                Date = today;
                BlockedToday = 0;
            }
        }
    }

    [DataContract]
    public class ShieldPidRecord
    {
        [DataMember(Name = "pid", Order = 1)]
        public int Pid { get; set; }

        [DataMember(Name = "path", Order = 2)]
        public string Path { get; set; }

        [DataMember(Name = "time", Order = 3)]
        public string Time { get; set; }
    }

    [DataContract]
    public class ShieldPidStore
    {
        [DataMember(Name = "pids", Order = 1)]
        public List<ShieldPidRecord> Pids { get; set; }

        [DataMember(Name = "pendingPath", Order = 2)]
        public string PendingPath { get; set; }

        [DataMember(Name = "pendingAt", Order = 3)]
        public string PendingAt { get; set; }

        public ShieldPidStore()
        {
            Pids = new List<ShieldPidRecord>();
            PendingPath = "";
            PendingAt = "";
        }
    }

    public class DetectedApp
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExePath { get; set; }
        public string Source { get; set; }
        public bool Installed { get; set; }

        public DetectedApp()
        {
            Id = "";
            Name = "";
            ExePath = "";
            Source = "";
            Installed = true;
        }
    }
}


