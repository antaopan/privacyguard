using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PrivacyGuard
{
    public class Engine
    {
        private readonly object _sync = new object();
        private Timer _timer;
        private AppConfig _config;
        private StatsData _stats;
        private bool _enabled;
        private DateTime _startedAt;
        private DateTime _lastConfigWrite = DateTime.MinValue;
        private DateTime _lastStatusWrite = DateTime.MinValue;
        private DateTime _lastRuleCheck = DateTime.MinValue;
        private DateTime _lastPolicyCheck = DateTime.MinValue;
        private DateTime _lastBlockedAt = DateTime.MinValue;
        private string _lastBlockedName = "";
        private string _lastEvent = "";
        private string _lastError = "";
        private string _appliedAt = "";
        private int _ruleCount;
        private int _hardeningApplied;
        private bool _firewallEnabled;
        private string _firewallDetail = "";
        private string _lastCommandId = "";
        private string _lastCommandRaw = "";
        private readonly HashSet<int> _seenPids = new HashSet<int>();
        private readonly HashSet<string> _rulePathsEnsured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _busy;
        private bool _stopping;

        public void Start()
        {
            AppPaths.EnsureDataDir();
            Logger.Info("service", "START", "PrivacyGuard 后台服务启动");
            _startedAt = DateTime.UtcNow;
            _stats = StatusStore.LoadStats();

            ServiceStatus previous = StatusStore.Load();
            if (previous != null)
            {
                _lastCommandId = previous.LastCommandId ?? "";
            }

            AppConfig config = null;
            try
            {
                ConfigStore.CreateDefaultIfMissing();
                string error;
                config = ConfigStore.Load(out error);
                if (config == null)
                {
                    config = new AppConfig();
                    _lastError = "配置读取失败，已使用默认配置：" + error;
                }
            }
            catch (Exception ex)
            {
                config = new AppConfig();
                _lastError = "配置读取异常：" + ex.Message;
                Logger.Error("service", "CONFIG_START_FAILED", ex.ToString());
            }

            lock (_sync)
            {
                ApplyConfig(config, "服务启动");
            }

            _timer = new Timer(Tick, null, 1000, 1500);
        }

        public void Stop()
        {
            lock (_sync)
            {
                _stopping = true;
            }

            if (_timer != null)
            {
                try { _timer.Dispose(); } catch { }
                _timer = null;
            }

            lock (_sync)
            {
                try
                {
                    FirewallManager.RemoveAll();
                    string policyError;
                    PolicyManager.RestoreAll(out policyError);
                    if (!string.IsNullOrEmpty(policyError))
                    {
                        Logger.Warn("service", "STOP_POLICY", policyError);
                    }
                    _ruleCount = 0;
                    _hardeningApplied = 0;
                    _enabled = false;
                    WriteStatusLocked(true, false);
                }
                catch (Exception ex)
                {
                    Logger.Error("service", "STOP_FAILED", ex.ToString());
                }
            }
            Logger.Info("service", "STOP", "PrivacyGuard 后台服务停止，已移除防火墙规则");
        }

        private void Tick(object state)
        {
            if (_stopping) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                lock (_sync)
                {
                    if (_stopping) return;
                    CheckConfigChange();
                    CheckCommand();

                    if (_enabled)
                    {
                        ScanProcesses();
                    }

                    if ((DateTime.UtcNow - _lastRuleCheck).TotalSeconds >= 20)
                    {
                        _lastRuleCheck = DateTime.UtcNow;
                        CheckFirewallHealth();
                    }

                    if (_enabled && (DateTime.UtcNow - _lastPolicyCheck).TotalMinutes >= 5)
                    {
                        _lastPolicyCheck = DateTime.UtcNow;
                        RepairPolicies();
                    }

                    WriteStatusLocked(false, true);
                }
            }
            catch (Exception ex)
            {
                _lastError = "服务循环异常：" + ex.Message;
                Logger.Error("service", "TICK_FAILED", ex.ToString());
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void CheckConfigChange()
        {
            DateTime write = ConfigStore.GetLastWriteTimeUtc();
            if (write <= _lastConfigWrite) return;

            string error;
            AppConfig config = ConfigStore.Load(out error);
            if (config == null)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    _lastError = "配置重载失败：" + error;
                    Logger.Error("service", "RELOAD_FAILED", error);
                }
                _lastConfigWrite = write;
                return;
            }

            if (_config != null && config.Revision == _config.Revision &&
                string.Equals(config.UpdatedAt, _config.UpdatedAt, StringComparison.Ordinal))
            {
                _lastConfigWrite = write;
                return;
            }

            ApplyConfig(config, "配置文件已更新");
        }

        private void CheckCommand()
        {
            try
            {
                if (!File.Exists(AppPaths.CommandFile)) return;
                string raw = JsonFile.ReadText(AppPaths.CommandFile);
                if (string.IsNullOrEmpty(raw) || string.Equals(raw, _lastCommandRaw, StringComparison.Ordinal)) return;

                ControlCommand command = null;
                try
                {
                    command = JsonFile.Read<ControlCommand>(AppPaths.CommandFile);
                }
                catch
                {
                    return;
                }
                if (command == null || string.IsNullOrEmpty(command.Id)) return;
                if (string.Equals(command.Id, _lastCommandId, StringComparison.OrdinalIgnoreCase))
                {
                    _lastCommandRaw = raw;
                    return;
                }

                _lastCommandRaw = raw;
                _lastCommandId = command.Id;
                Logger.Info("service", "COMMAND", command.Command);

                if (string.Equals(command.Command, "panic", StringComparison.OrdinalIgnoreCase))
                {
                    AppConfig current = _config ?? ConfigStore.LoadOrDefault();
                    current.Enabled = true;
                    current.Touch();
                    ConfigStore.Save(current);
                    _lastConfigWrite = ConfigStore.GetLastWriteTimeUtc();
                    KillAllMatching(current, true);
                    ApplyConfig(current, "立即隔离");
                }
                else if (string.Equals(command.Command, "restore", StringComparison.OrdinalIgnoreCase))
                {
                    string err;
                    PolicyManager.RestoreAll(out err);
                    _hardeningApplied = 0;
                    _lastError = err ?? "";
                }
                else
                {
                    string error;
                    AppConfig current = ConfigStore.Load(out error);
                    if (current != null) ApplyConfig(current, "控制命令 " + command.Command);
                    else if (!string.IsNullOrEmpty(error)) _lastError = error;
                }

                WriteStatusLocked(true, true);
            }
            catch (Exception ex)
            {
                _lastError = "处理控制命令失败：" + ex.Message;
                Logger.Error("service", "COMMAND_FAILED", ex.ToString());
            }
        }

        private void ApplyConfig(AppConfig config, string reason)
        {
            if (config == null) config = new AppConfig();
            config.Normalize();
            _config = config;
            _enabled = config.Enabled;
            _rulePathsEnsured.Clear();
            _lastError = "";

            if (_enabled)
            {
                FirewallPlan plan = FirewallManager.BuildPlan(config);
                FirewallApplyResult fw = FirewallManager.Apply(plan);
                _ruleCount = fw.Total;
                if (!string.IsNullOrEmpty(fw.Error)) _lastError = fw.Error;

                string policyError;
                _hardeningApplied = PolicyManager.Apply(config.Hardening, true, out policyError);
                if (!string.IsNullOrEmpty(policyError)) _lastError = AppendError(_lastError, policyError);
            }
            else
            {
                FirewallManager.RemoveAll();
                _ruleCount = 0;
                string policyError;
                PolicyManager.RestoreAll(out policyError);
                _hardeningApplied = 0;
                if (!string.IsNullOrEmpty(policyError)) _lastError = policyError;
            }

            string fwDetail;
            _firewallEnabled = FirewallManager.IsFirewallEnabled(out fwDetail);
            _firewallDetail = fwDetail;
            _appliedAt = DateTime.UtcNow.ToString("o");
            _lastConfigWrite = ConfigStore.GetLastWriteTimeUtc();
            _lastRuleCheck = DateTime.UtcNow;
            _lastPolicyCheck = DateTime.UtcNow;
            _lastEvent = reason;
            WriteStatusLocked(true, true);
            Logger.Info("service", "APPLIED",
                string.Format("{0} · 启用={1} · 应用={2} · 规则={3} · 加固={4}",
                    reason, _enabled, config.Apps == null ? 0 : config.Apps.Count, _ruleCount, _hardeningApplied));
        }

        private static string AppendError(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "；" + b;
        }

        private void ScanProcesses()
        {
            if (_config == null || _config.Apps == null || _config.Apps.Count == 0) return;

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }

            try
            {
                int self = Process.GetCurrentProcess().Id;
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    if (process == null) continue;
                    if (process.Id <= 4 || process.Id == self) continue;

                    string path;
                    try
                    {
                        path = process.MainModule.FileName;
                    }
                    catch
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(path)) continue;
                    path = AppPaths.CleanPath(path);

                    for (int a = 0; a < _config.Apps.Count; a++)
                    {
                        AppRule app = _config.Apps[a];
                        if (app == null || !app.Enabled) continue;
                        if (!AppMatcher.Matches(app, path)) continue;

                        if (_seenPids.Contains(process.Id)) break;

                        if (ShieldRegistry.IsAllowed(process.Id, path))
                        {
                            _seenPids.Add(process.Id);
                            break;
                        }

                        _seenPids.Add(process.Id);

                        if (!_rulePathsEnsured.Contains(path) && app.Network != NetworkModes.Off)
                        {
                            _rulePathsEnsured.Add(path);
                            List<FirewallRuleSpec> specs = FirewallManager.BuildSpecsForExe(app, path);
                            for (int s = 0; s < specs.Count; s++)
                            {
                                FirewallManager.EnsureRule(specs[s]);
                            }
                        }

                        _stats.RollIfNeeded();
                        _stats.BlockedToday++;
                        _stats.BlockedTotal++;
                        _lastBlockedAt = DateTime.UtcNow;
                        _lastBlockedName = app.Name;
                        string action = "检测到";
                        if (app.Kill)
                        {
                            try
                            {
                                process.Kill();
                                process.WaitForExit(800);
                                action = "已终止";
                            }
                            catch (Exception ex)
                            {
                                action = "终止失败";
                                Logger.Warn("service", "KILL_FAILED", app.Name + " pid=" + process.Id + " · " + ex.Message);
                            }
                        }

                        _lastEvent = action + "黑名单应用 " + app.Name;
                        Logger.Block("service", "BLOCKED_APP",
                            string.Format("{0} · {1} · pid={2} · 网络={3}", action, path, process.Id, NetworkModes.Label(app.Network)));
                        StatusStore.SaveStats(_stats);
                        break;
                    }
                }

                if (_seenPids.Count > 10000) _seenPids.Clear();
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try { processes[i].Dispose(); } catch { }
                }
            }
        }

        private void KillAllMatching(AppConfig config, bool includeShield)
        {
            if (config == null || config.Apps == null) return;
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }
            try
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    if (process == null || process.Id <= 4) continue;
                    string path;
                    try { path = process.MainModule.FileName; }
                    catch { continue; }
                    path = AppPaths.CleanPath(path);

                    for (int a = 0; a < config.Apps.Count; a++)
                    {
                        AppRule app = config.Apps[a];
                        if (app == null || !app.Enabled) continue;
                        if (!AppMatcher.Matches(app, path)) continue;
                        if (!includeShield && ShieldRegistry.IsAllowed(process.Id, path)) break;
                        try
                        {
                            process.Kill();
                            process.WaitForExit(800);
                            _stats.RollIfNeeded();
                            _stats.BlockedToday++;
                            _stats.BlockedTotal++;
                            _lastBlockedAt = DateTime.UtcNow;
                            _lastBlockedName = app.Name;
                            _lastEvent = "立即隔离：已终止 " + app.Name;
                            Logger.Block("service", "PANIC_KILL", app.Name + " · " + path + " · pid=" + process.Id);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("service", "PANIC_KILL_FAILED", app.Name + " · " + ex.Message);
                        }
                        break;
                    }
                }
                StatusStore.SaveStats(_stats);
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    try { processes[i].Dispose(); } catch { }
                }
            }
        }

        private void CheckFirewallHealth()
        {
            try
            {
                string detail;
                _firewallEnabled = FirewallManager.IsFirewallEnabled(out detail);
                _firewallDetail = detail;

                if (!_enabled) return;
                FirewallPlan plan = FirewallManager.BuildPlan(_config);
                int actual = FirewallManager.GetGroupRuleCount();
                if (actual != plan.Count)
                {
                    Logger.Warn("service", "RULE_REPAIR",
                        string.Format("规则数量不一致（期望 {0} / 实际 {1}），正在重建", plan.Count, actual));
                    FirewallApplyResult result = FirewallManager.Apply(plan);
                    _ruleCount = result.Total;
                    if (!string.IsNullOrEmpty(result.Error)) _lastError = result.Error;
                }
                else
                {
                    _ruleCount = actual;
                }
            }
            catch (Exception ex)
            {
                _lastError = "检查防火墙状态失败：" + ex.Message;
                Logger.Error("service", "FIREWALL_HEALTH_FAILED", ex.ToString());
            }
        }

        private void RepairPolicies()
        {
            if (_config == null) return;
            try
            {
                int desired = PolicyManager.DesiredCount(_config.Hardening, _enabled);
                if (desired == 0) return;
                int actual = 0;
                foreach (PolicyDefinition def in PolicyManager.GetCatalog())
                {
                    if (_config.Hardening.Get(def.Id) && PolicyManager.IsApplied(def.Id, _config.Hardening, _enabled))
                    {
                        actual++;
                    }
                }
                if (actual < desired)
                {
                    string error;
                    _hardeningApplied = PolicyManager.Apply(_config.Hardening, _enabled, out error);
                    if (!string.IsNullOrEmpty(error)) _lastError = error;
                    Logger.Warn("policy", "POLICY_REPAIR",
                        string.Format("加固策略被修改（{0}/{1} 项），已重新应用", actual, desired));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("policy", "POLICY_REPAIR_FAILED", ex.ToString());
            }
        }

        private void WriteStatusLocked(bool force, bool running)
        {
            if (!force && (DateTime.UtcNow - _lastStatusWrite).TotalSeconds < 3) return;
            try
            {
                ServiceStatus status = new ServiceStatus();
                status.ServiceRunning = running;
                status.ProtectionEnabled = _enabled && _config != null && _config.Enabled;
                status.ServiceStartedAt = _startedAt.ToString("o");
                status.AppliedAt = _appliedAt;
                status.ConfigRevision = _config == null ? 0 : _config.Revision;
                status.AppCount = _config == null || _config.Apps == null ? 0 : _config.Apps.Count;
                status.EnabledAppCount = _config == null || _config.Apps == null ? 0 :
                    _config.Apps.Count(x => x != null && x.Enabled);
                status.RuleCount = _ruleCount;
                status.BlockedToday = _stats == null ? 0 : _stats.BlockedToday;
                status.BlockedTotal = _stats == null ? 0 : _stats.BlockedTotal;
                status.FirewallEnabled = _firewallEnabled;
                status.FirewallDetail = _firewallDetail;
                status.HardeningApplied = _hardeningApplied;
                status.LastError = _lastError;
                status.LastEvent = _lastEvent;
                status.LastBlockedName = _lastBlockedName;
                status.LastBlockedAt = _lastBlockedAt == DateTime.MinValue ? "" : _lastBlockedAt.ToString("o");
                status.LastCommandId = _lastCommandId;
                status.UptimeSeconds = (long)(DateTime.UtcNow - _startedAt).TotalSeconds;
                StatusStore.Save(status);
                _lastStatusWrite = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error("service", "STATUS_WRITE_FAILED", ex.ToString());
            }
        }
    }
}
