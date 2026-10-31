using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PrivacyGuard
{
    public class FirewallRuleSpec
    {
        public string Name { get; set; }
        public string AppId { get; set; }
        public string AppName { get; set; }
        public string ExePath { get; set; }
        public int Direction { get; set; }
        public string RemoteAddresses { get; set; }
        public string Mode { get; set; }

        public FirewallRuleSpec()
        {
            Name = "";
            AppId = "";
            AppName = "";
            ExePath = "";
            RemoteAddresses = "*";
            Mode = NetworkModes.Full;
        }
    }

    public class FirewallPlan
    {
        public List<FirewallRuleSpec> Rules { get; set; }

        public FirewallPlan()
        {
            Rules = new List<FirewallRuleSpec>();
        }

        public int Count
        {
            get { return Rules == null ? 0 : Rules.Count; }
        }
    }

    public class FirewallApplyResult
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Total { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }

        public FirewallApplyResult()
        {
            Error = "";
        }
    }

    public static class FirewallManager
    {
        private static readonly object Sync = new object();
        private const int DirectionInbound = 1;
        private const int DirectionOutbound = 2;
        private const int ActionBlock = 0;
        private const int ProtocolAny = 256;
        private const int ProfilesAll = 0x7FFFFFFF;

        public static FirewallPlan BuildPlan(AppConfig config)
        {
            FirewallPlan plan = new FirewallPlan();
            if (config == null || !config.Enabled || config.Apps == null) return plan;

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Apps.Count; i++)
            {
                AppRule app = config.Apps[i];
                if (app == null || !app.Enabled || app.Network == NetworkModes.Off) continue;

                List<string> targets = AppMatcher.Targets(app);
                for (int t = 0; t < targets.Count; t++)
                {
                    List<FirewallRuleSpec> specs = BuildSpecsForExe(app, targets[t]);
                    for (int s = 0; s < specs.Count; s++)
                    {
                        Add(plan, names, specs[s]);
                    }
                }
            }

            plan.Rules.Sort(delegate(FirewallRuleSpec a, FirewallRuleSpec b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return plan;
        }

        private static void Add(FirewallPlan plan, HashSet<string> names, FirewallRuleSpec spec)
        {
            if (names.Add(spec.Name)) plan.Rules.Add(spec);
        }

        public static List<FirewallRuleSpec> BuildSpecsForExe(AppRule app, string exe)
        {
            List<FirewallRuleSpec> specs = new List<FirewallRuleSpec>();
            if (app == null || string.IsNullOrEmpty(exe) || app.Network == NetworkModes.Off) return specs;

            string hash = ShortHash(exe);
            string baseName = "PrivacyGuard-" + SafeId(app.Id) + "-" + hash + "-" + app.Network;

            if (app.Network == NetworkModes.Full || app.Network == NetworkModes.Lan)
            {
                string remote = app.Network == NetworkModes.Lan ? "LocalSubnet" : "*";
                specs.Add(new FirewallRuleSpec
                {
                    Name = baseName + "-out",
                    AppId = app.Id,
                    AppName = app.Name,
                    ExePath = exe,
                    Direction = DirectionOutbound,
                    RemoteAddresses = remote,
                    Mode = app.Network
                });
                specs.Add(new FirewallRuleSpec
                {
                    Name = baseName + "-in",
                    AppId = app.Id,
                    AppName = app.Name,
                    ExePath = exe,
                    Direction = DirectionInbound,
                    RemoteAddresses = "*",
                    Mode = app.Network
                });
            }
            else if (app.Network == NetworkModes.Inbound)
            {
                specs.Add(new FirewallRuleSpec
                {
                    Name = baseName + "-in",
                    AppId = app.Id,
                    AppName = app.Name,
                    ExePath = exe,
                    Direction = DirectionInbound,
                    RemoteAddresses = "*",
                    Mode = app.Network
                });
            }
            return specs;
        }

        public static FirewallApplyResult Apply(FirewallPlan plan)
        {
            FirewallApplyResult result = new FirewallApplyResult();
            lock (Sync)
            {
                dynamic policy = null;
                try
                {
                    policy = CreatePolicy();
                    dynamic rules = policy.Rules;
                    Dictionary<string, object> existing = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    foreach (object item in (System.Collections.IEnumerable)rules)
                    {
                        dynamic rule = item;
                        string grouping = "";
                        string name = "";
                        try
                        {
                            grouping = rule.Grouping as string;
                            name = rule.Name as string;
                        }
                        catch
                        {
                            continue;
                        }
                        if (string.Equals(grouping, ProductInfo.FirewallGroup, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(name))
                        {
                            if (!existing.ContainsKey(name)) existing[name] = item;
                        }
                    }

                    foreach (string oldName in existing.Keys.ToArray())
                    {
                        if (!plan.Rules.Any(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase)))
                        {
                            try
                            {
                                rules.Remove(oldName);
                                result.Removed++;
                            }
                            catch (Exception ex)
                            {
                                result.Error = "删除旧规则失败: " + ex.Message;
                            }
                        }
                    }

                    foreach (FirewallRuleSpec spec in plan.Rules)
                    {
                        if (existing.ContainsKey(spec.Name)) continue;
                        try
                        {
                            AddRule(rules, spec);
                            result.Added++;
                        }
                        catch (Exception ex)
                        {
                            result.Error = "创建规则失败: " + spec.Name + " · " + ex.Message;
                            Logger.Error("firewall", "ADD_FAILED", result.Error);
                        }
                    }

                    result.Total = CountGroupRules(rules);
                    result.Success = string.IsNullOrEmpty(result.Error);
                    Logger.Info("firewall", "APPLIED",
                        string.Format("规则计划 {0} 条，新增 {1}，移除 {2}，当前 {3}", plan.Count, result.Added, result.Removed, result.Total));
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    Logger.Error("firewall", "APPLY_FAILED", ex.ToString());
                }
                finally
                {
                    Release(policy);
                }
            }
            return result;
        }

        public static FirewallApplyResult RemoveAll()
        {
            FirewallApplyResult result = new FirewallApplyResult();
            lock (Sync)
            {
                dynamic policy = null;
                try
                {
                    policy = CreatePolicy();
                    dynamic rules = policy.Rules;
                    List<string> names = new List<string>();
                    foreach (object item in (System.Collections.IEnumerable)rules)
                    {
                        dynamic rule = item;
                        try
                        {
                            string grouping = rule.Grouping as string;
                            string name = rule.Name as string;
                            if (string.Equals(grouping, ProductInfo.FirewallGroup, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(name))
                            {
                                names.Add(name);
                            }
                        }
                        catch
                        {
                        }
                    }
                    foreach (string name in names)
                    {
                        try
                        {
                            rules.Remove(name);
                            result.Removed++;
                        }
                        catch (Exception ex)
                        {
                            result.Error = ex.Message;
                        }
                    }
                    result.Success = string.IsNullOrEmpty(result.Error);
                    Logger.Info("firewall", "REMOVED_ALL", "移除 " + result.Removed + " 条规则");
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    Logger.Error("firewall", "REMOVE_ALL_FAILED", ex.ToString());
                }
                finally
                {
                    Release(policy);
                }
            }
            return result;
        }

        public static bool IsFirewallEnabled(out string detail)
        {
            detail = "";
            dynamic policy = null;
            try
            {
                policy = CreatePolicy();
                bool domain = GetProfileEnabled(policy, 1);
                bool privateProfile = GetProfileEnabled(policy, 2);
                bool publicProfile = GetProfileEnabled(policy, 4);
                List<string> on = new List<string>();
                List<string> off = new List<string>();
                (domain ? on : off).Add("域");
                (privateProfile ? on : off).Add("专用");
                (publicProfile ? on : off).Add("公用");
                if (off.Count == 0) detail = "已启用（域 / 专用 / 公用）";
                else detail = "未启用：" + string.Join("、", off.ToArray());
                return off.Count == 0;
            }
            catch (Exception ex)
            {
                detail = "无法读取防火墙状态：" + ex.Message;
                return false;
            }
            finally
            {
                Release(policy);
            }
        }

        public static int GetGroupRuleCount()
        {
            dynamic policy = null;
            try
            {
                policy = CreatePolicy();
                return CountGroupRules(policy.Rules);
            }
            catch
            {
                return 0;
            }
            finally
            {
                Release(policy);
            }
        }

        public static FirewallApplyResult EnsureRule(FirewallRuleSpec spec)
        {
            FirewallApplyResult result = new FirewallApplyResult();
            if (spec == null) return result;
            lock (Sync)
            {
                dynamic policy = null;
                try
                {
                    policy = CreatePolicy();
                    dynamic rules = policy.Rules;
                    bool exists = false;
                    foreach (object item in (System.Collections.IEnumerable)rules)
                    {
                        dynamic rule = item;
                        try
                        {
                            string name = rule.Name as string;
                            if (string.Equals(name, spec.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }
                    if (!exists)
                    {
                        AddRule(rules, spec);
                        result.Added = 1;
                        Logger.Info("firewall", "RULE_ADDED_NOW", spec.AppName + " · " + spec.ExePath);
                    }
                    result.Total = CountGroupRules(rules);
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                finally
                {
                    Release(policy);
                }
            }
            return result;
        }

        private static void AddRule(dynamic rules, FirewallRuleSpec spec)
        {
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwRule"));
            rule.Name = spec.Name;
            rule.Description = "PrivacyGuard · " + spec.AppName + " · " + NetworkModes.Label(spec.Mode);
            rule.ApplicationName = spec.ExePath;
            rule.Action = ActionBlock;
            rule.Direction = spec.Direction;
            rule.Protocol = ProtocolAny;
            rule.Profiles = ProfilesAll;
            rule.Enabled = true;
            rule.Grouping = ProductInfo.FirewallGroup;
            rule.InterfaceTypes = "All";
            rule.EdgeTraversal = false;
            if (!string.IsNullOrEmpty(spec.RemoteAddresses)) rule.RemoteAddresses = spec.RemoteAddresses;
            rules.Add(rule);
        }

        private static dynamic CreatePolicy()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null) throw new InvalidOperationException("Windows 防火墙 COM 组件不可用");
            return Activator.CreateInstance(type);
        }

        private static bool GetProfileEnabled(dynamic policy, int profile)
        {
            return (bool)policy.FirewallEnabled[profile];
        }

        private static int CountGroupRules(dynamic rules)
        {
            int count = 0;
            foreach (object item in (System.Collections.IEnumerable)rules)
            {
                dynamic rule = item;
                try
                {
                    string grouping = rule.Grouping as string;
                    if (string.Equals(grouping, ProductInfo.FirewallGroup, StringComparison.OrdinalIgnoreCase)) count++;
                }
                catch
                {
                }
            }
            return count;
        }

        private static string ShortHash(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
                StringBuilder sb = new StringBuilder(10);
                for (int i = 0; i < 5; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string SafeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "app";
            StringBuilder sb = new StringBuilder();
            foreach (char c in id)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? "app" : sb.ToString();
        }

        private static void Release(object comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
            }
        }
    }
}
