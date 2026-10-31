using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyGuard
{
    public class AutoEnrollmentResult
    {
        public bool Changed { get; set; }
        public List<string> AddedNames { get; set; }
        public List<string> MarkedNames { get; set; }
        public List<string> UpdatedNames { get; set; }

        public AutoEnrollmentResult()
        {
            AddedNames = new List<string>();
            MarkedNames = new List<string>();
            UpdatedNames = new List<string>();
        }
    }

    /// <summary>
    /// 只自动纳入 Claude / Gemini / Codex 三类应用。
    /// 其他 AI 应用必须由用户手动添加到黑名单。
    /// </summary>
    public static class AutoEnrollment
    {
        public static AutoEnrollmentResult Ensure(AppConfig config)
        {
            AutoEnrollmentResult result = new AutoEnrollmentResult();
            if (config == null) return result;
            config.Normalize();

            List<DetectedApp> detected = AppDiscovery.Detect()
                .Where(a => a != null && AppDiscovery.IsAutoManaged(a.Id))
                .ToList();

            HashSet<string> handledKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DetectedApp app in detected)
            {
                if (!handledKinds.Add(app.Id)) continue;

                bool alreadyAutoAdded = config.AutoAdded.Any(x =>
                    string.Equals(x, app.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x, app.Name, StringComparison.OrdinalIgnoreCase));
                if (alreadyAutoAdded)
                {
                    // Codex 的安装目录含版本哈希，升级后路径会变化；自动规则应跟随更新。
                    AppRule autoRule = config.Apps.FirstOrDefault(a =>
                        a != null && string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase));
                    if (autoRule != null && !string.Equals(AppPaths.CleanPath(autoRule.Path), AppPaths.CleanPath(app.ExePath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        autoRule.Path = app.ExePath;
                        if (autoRule.Scope == Scopes.Folder)
                        {
                            autoRule.Root = AppMatcher.InferRoot(app.ExePath);
                        }
                        autoRule.Normalize();
                        result.UpdatedNames.Add(app.Name);
                        result.Changed = true;
                    }
                    continue;
                }

                AppRule existing = config.Apps.FirstOrDefault(a =>
                    a != null && AppMatcher.Matches(a, app.ExePath));
                if (existing != null)
                {
                    config.AutoAdded.Add(app.Id);
                    result.MarkedNames.Add(app.Name);
                    result.Changed = true;
                    continue;
                }

                if (string.Equals(app.Id, "codex", StringComparison.OrdinalIgnoreCase))
                {
                    AppRule codex = CreateRule(app, Scopes.File);
                    config.Apps.Add(codex);
                }
                else
                {
                    string root = AppMatcher.InferRoot(app.ExePath);
                    AppRule rule = string.IsNullOrEmpty(root)
                        ? CreateRule(app, Scopes.File)
                        : CreateRule(app, Scopes.Folder);
                    config.Apps.Add(rule);
                }

                config.AutoAdded.Add(app.Id);
                result.AddedNames.Add(app.Name);
                result.Changed = true;
            }

            if (result.Changed) config.Touch();
            return result;
        }

        private static AppRule CreateRule(DetectedApp app, string scope)
        {
            AppRule rule = new AppRule();
            rule.Name = app.Name;
            rule.Path = app.ExePath;
            rule.Scope = scope;
            rule.Root = scope == Scopes.Folder ? AppMatcher.InferRoot(app.ExePath) : "";
            rule.Network = NetworkModes.Inbound;
            rule.Shield = true;
            rule.Kill = false;
            rule.Enabled = true;
            rule.Normalize();
            return rule;
        }
    }
}
