using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace PrivacyGuard
{
    public class PolicyValue
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public object Value { get; set; }
        public RegistryValueKind Kind { get; set; }

        public PolicyValue(string key, string name, object value)
        {
            Key = key;
            Name = name;
            Value = value;
            Kind = value is int ? RegistryValueKind.DWord : RegistryValueKind.String;
        }
    }

    public class PolicyDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Recommended { get; set; }
        public bool NeedsSignOut { get; set; }
        public List<PolicyValue> Values { get; set; }

        public PolicyDefinition()
        {
            Values = new List<PolicyValue>();
            Id = "";
            Title = "";
            Description = "";
        }
    }

    [DataContract]
    public class PolicyBackupEntry
    {
        [DataMember(Name = "key", Order = 1)]
        public string Key { get; set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        [DataMember(Name = "exists", Order = 3)]
        public bool Exists { get; set; }

        [DataMember(Name = "kind", Order = 4)]
        public int Kind { get; set; }

        [DataMember(Name = "stringValue", Order = 5)]
        public string StringValue { get; set; }

        [DataMember(Name = "intValue", Order = 6)]
        public int IntValue { get; set; }

        public PolicyBackupEntry()
        {
            Key = "";
            Name = "";
            StringValue = "";
            Kind = 1;
        }
    }

    [DataContract]
    public class PolicyBackupStore
    {
        [DataMember(Name = "entries", Order = 1)]
        public List<PolicyBackupEntry> Entries { get; set; }

        public PolicyBackupStore()
        {
            Entries = new List<PolicyBackupEntry>();
        }
    }

    public static class PolicyManager
    {
        private static readonly object Sync = new object();

        public static List<PolicyDefinition> GetCatalog()
        {
            List<PolicyDefinition> list = new List<PolicyDefinition>();

            PolicyDefinition recall = new PolicyDefinition();
            recall.Id = "recall";
            recall.Title = "Windows Recall / AI 数据采集";
            recall.Description = "关闭 Recall 屏幕快照与 Windows AI 数据收集策略，阻止系统持续记录你的屏幕内容。";
            recall.Recommended = true;
            recall.NeedsSignOut = true;
            recall.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            recall.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0));
            list.Add(recall);

            PolicyDefinition copilot = new PolicyDefinition();
            copilot.Id = "copilot";
            copilot.Title = "Windows Copilot";
            copilot.Description = "通过组策略关闭 Windows Copilot，避免系统级 AI 助手参与内容读取与云端交互。";
            copilot.Recommended = true;
            copilot.NeedsSignOut = false;
            copilot.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            list.Add(copilot);

            PolicyDefinition clipboard = new PolicyDefinition();
            clipboard.Id = "clipboard";
            clipboard.Title = "剪贴板历史与跨设备云同步";
            clipboard.Description = "关闭剪贴板历史记录、跨设备同步和云端上传，降低密码、API Key 等敏感内容被其他应用读取的机会。";
            clipboard.Recommended = true;
            clipboard.NeedsSignOut = false;
            clipboard.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", 0));
            clipboard.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0));
            list.Add(clipboard);

            PolicyDefinition advertising = new PolicyDefinition();
            advertising.Id = "advertising";
            advertising.Title = "广告 ID 与个性化跟踪";
            advertising.Description = "禁用系统广告 ID，减少应用通过设备标识进行跨应用关联。";
            advertising.Recommended = true;
            advertising.NeedsSignOut = false;
            advertising.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1));
            list.Add(advertising);

            PolicyDefinition activity = new PolicyDefinition();
            activity.Id = "activity";
            activity.Title = "活动历史与时间线";
            activity.Description = "停止发布、上传和同步用户活动记录，避免 AI 应用从系统时间线推断你的工作内容。";
            activity.Recommended = true;
            activity.NeedsSignOut = false;
            activity.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0));
            activity.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0));
            activity.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            list.Add(activity);

            PolicyDefinition cloudsearch = new PolicyDefinition();
            cloudsearch.Id = "cloudsearch";
            cloudsearch.Title = "搜索云内容与网络建议";
            cloudsearch.Description = "关闭 Windows 搜索的云端结果和网络建议，减少搜索关键词离开本机。";
            cloudsearch.Recommended = true;
            cloudsearch.NeedsSignOut = false;
            cloudsearch.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCloudSearch", 0));
            cloudsearch.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0));
            list.Add(cloudsearch);

            PolicyDefinition telemetry = new PolicyDefinition();
            telemetry.Id = "telemetry";
            telemetry.Title = "诊断数据与反馈";
            telemetry.Description = "将系统诊断数据限制在最低级别。企业环境可能由组织策略覆盖。";
            telemetry.Recommended = false;
            telemetry.NeedsSignOut = true;
            telemetry.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            list.Add(telemetry);

            PolicyDefinition location = new PolicyDefinition();
            location.Id = "location";
            location.Title = "位置服务";
            location.Description = "关闭系统位置服务并拒绝应用访问位置。可能影响地图、天气和“查找我的设备”。";
            location.Recommended = false;
            location.NeedsSignOut = false;
            location.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1));
            location.Values.Add(new PolicyValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny"));
            list.Add(location);

            PolicyDefinition cameraMic = new PolicyDefinition();
            cameraMic.Id = "cameramic";
            cameraMic.Title = "摄像头与麦克风";
            cameraMic.Description = "强制拒绝应用访问摄像头和麦克风，防止 AI 应用在后台采集音视频。会影响会议与通话。";
            cameraMic.Recommended = false;
            cameraMic.NeedsSignOut = false;
            cameraMic.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessCamera", 2));
            cameraMic.Values.Add(new PolicyValue(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMicrophone", 2));
            cameraMic.Values.Add(new PolicyValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam", "Value", "Deny"));
            cameraMic.Values.Add(new PolicyValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", "Value", "Deny"));
            list.Add(cameraMic);

            return list;
        }

        public static PolicyDefinition Find(string id)
        {
            return GetCatalog().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static int DesiredCount(HardeningConfig config, bool masterEnabled)
        {
            if (!masterEnabled || config == null) return 0;
            int count = 0;
            foreach (PolicyDefinition def in GetCatalog())
            {
                if (config.Get(def.Id)) count++;
            }
            return count;
        }

        public static int Apply(HardeningConfig config, bool masterEnabled, out string error)
        {
            error = "";
            lock (Sync)
            {
                if (!masterEnabled)
                {
                    return RestoreAllLocked(out error);
                }

                List<PolicyDefinition> catalog = GetCatalog();
                List<PolicyDefinition> desired = catalog.Where(d => config != null && config.Get(d.Id)).ToList();
                List<PolicyValue> desiredValues = desired.SelectMany(d => d.Values).ToList();
                HashSet<string> desiredKeys = new HashSet<string>(
                    desiredValues.Select(v => EntryKey(v.Key, v.Name)), StringComparer.OrdinalIgnoreCase);

                PolicyBackupStore backup = LoadBackup();
                bool backupChanged = false;

                foreach (PolicyValue value in desiredValues)
                {
                    string key = EntryKey(value.Key, value.Name);
                    if (backup.Entries.Any(x => string.Equals(EntryKey(x.Key, x.Name), key, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    PolicyBackupEntry entry = ReadEntry(value.Key, value.Name);
                    backup.Entries.Add(entry);
                    backupChanged = true;
                }

                if (backupChanged)
                {
                    SaveBackup(backup);
                }

                int appliedDefinitions = 0;
                foreach (PolicyDefinition def in desired)
                {
                    bool ok = true;
                    foreach (PolicyValue value in def.Values)
                    {
                        try
                        {
                            WriteValue(value);
                        }
                        catch (Exception ex)
                        {
                            ok = false;
                            error = "写入 " + value.Key + "\\" + value.Name + " 失败：" + ex.Message;
                            Logger.Error("policy", "WRITE_FAILED", error);
                        }
                    }
                    if (ok) appliedDefinitions++;
                }

                List<PolicyBackupEntry> remove = new List<PolicyBackupEntry>();
                foreach (PolicyBackupEntry entry in backup.Entries)
                {
                    if (!desiredKeys.Contains(EntryKey(entry.Key, entry.Name)))
                    {
                        try
                        {
                            RestoreEntry(entry);
                            remove.Add(entry);
                        }
                        catch (Exception ex)
                        {
                            error = "恢复 " + entry.Key + "\\" + entry.Name + " 失败：" + ex.Message;
                            Logger.Error("policy", "RESTORE_FAILED", error);
                        }
                    }
                }
                if (remove.Count > 0)
                {
                    foreach (PolicyBackupEntry entry in remove) backup.Entries.Remove(entry);
                    SaveBackup(backup);
                }

                Logger.Info("policy", "APPLIED",
                    string.Format("已应用 {0}/{1} 项系统加固策略", appliedDefinitions, desired.Count));
                return appliedDefinitions;
            }
        }

        public static int RestoreAll(out string error)
        {
            lock (Sync)
            {
                return RestoreAllLocked(out error);
            }
        }

        private static int RestoreAllLocked(out string error)
        {
            error = "";
            int restored = 0;
            try
            {
                PolicyBackupStore backup = LoadBackup();
                List<PolicyBackupEntry> failed = new List<PolicyBackupEntry>();
                foreach (PolicyBackupEntry entry in backup.Entries)
                {
                    try
                    {
                        RestoreEntry(entry);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(entry);
                        error = ex.Message;
                        Logger.Error("policy", "RESTORE_FAILED", entry.Key + "\\" + entry.Name + " · " + ex.Message);
                    }
                }
                backup.Entries = failed;
                SaveBackup(backup);
                Logger.Info("policy", "RESTORED", "恢复 " + restored + " 项系统设置");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Error("policy", "RESTORE_ALL_FAILED", ex.ToString());
            }
            return restored;
        }

        public static bool IsApplied(string id, HardeningConfig config, bool masterEnabled)
        {
            if (!masterEnabled || config == null || !config.Get(id)) return false;
            PolicyDefinition def = Find(id);
            if (def == null) return false;
            foreach (PolicyValue value in def.Values)
            {
                try
                {
                    object current = ReadValue(value.Key, value.Name);
                    if (!ValueEquals(current, value.Value)) return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private static string EntryKey(string key, string name)
        {
            return (key ?? "").ToLowerInvariant() + "|" + (name ?? "").ToLowerInvariant();
        }

        private static PolicyBackupEntry ReadEntry(string key, string name)
        {
            PolicyBackupEntry entry = new PolicyBackupEntry();
            entry.Key = key;
            entry.Name = name;
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey sub = baseKey.OpenSubKey(key, false))
            {
                if (sub == null)
                {
                    entry.Exists = false;
                    return entry;
                }
                object value = sub.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null)
                {
                    entry.Exists = false;
                    return entry;
                }
                entry.Exists = true;
                entry.Kind = (int)sub.GetValueKind(name);
                if (value is int) entry.IntValue = (int)value;
                else if (value is long) entry.IntValue = unchecked((int)(long)value);
                else entry.StringValue = Convert.ToString(value);
            }
            return entry;
        }

        private static void WriteValue(PolicyValue value)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey sub = baseKey.CreateSubKey(value.Key, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (sub == null) throw new InvalidOperationException("无法创建注册表键");
                if (value.Kind == RegistryValueKind.DWord)
                {
                    sub.SetValue(value.Name, Convert.ToInt32(value.Value), RegistryValueKind.DWord);
                }
                else
                {
                    sub.SetValue(value.Name, Convert.ToString(value.Value), RegistryValueKind.String);
                }
            }
        }

        private static object ReadValue(string key, string name)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey sub = baseKey.OpenSubKey(key, false))
            {
                if (sub == null) return null;
                return sub.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
        }

        private static bool ValueEquals(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a is int && b is int) return (int)a == (int)b;
            return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.OrdinalIgnoreCase);
        }

        private static void RestoreEntry(PolicyBackupEntry entry)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey sub = baseKey.OpenSubKey(entry.Key, true))
            {
                if (sub == null) return;
                if (!entry.Exists)
                {
                    sub.DeleteValue(entry.Name, false);
                }
                else if (entry.Kind == (int)RegistryValueKind.DWord)
                {
                    sub.SetValue(entry.Name, entry.IntValue, RegistryValueKind.DWord);
                }
                else if (entry.Kind == (int)RegistryValueKind.String || entry.Kind == (int)RegistryValueKind.ExpandString)
                {
                    sub.SetValue(entry.Name, entry.StringValue ?? "", (RegistryValueKind)entry.Kind);
                }
            }
        }

        private static PolicyBackupStore LoadBackup()
        {
            try
            {
                PolicyBackupStore store = JsonFile.Read<PolicyBackupStore>(AppPaths.MachineBackupFile);
                if (store == null) store = new PolicyBackupStore();
                if (store.Entries == null) store.Entries = new List<PolicyBackupEntry>();
                return store;
            }
            catch
            {
                return new PolicyBackupStore();
            }
        }

        private static void SaveBackup(PolicyBackupStore store)
        {
            if (store.Entries == null || store.Entries.Count == 0)
            {
                try
                {
                    if (File.Exists(AppPaths.MachineBackupFile)) File.Delete(AppPaths.MachineBackupFile);
                }
                catch
                {
                }
                return;
            }
            JsonFile.Write(AppPaths.MachineBackupFile, store);
        }
    }
}
