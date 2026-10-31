using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PrivacyGuard
{
    public static class AppMatcher
    {
        private static readonly Regex VersionFolder = new Regex(
            @"^(app-|v?\d+\.\d+|\d{4,})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Uninstaller = new Regex(
            @"^(unins|uninstall|setup|installer)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool Matches(AppRule rule, string exePath)
        {
            if (rule == null) return false;
            string path = AppPaths.CleanPath(exePath);
            if (string.IsNullOrEmpty(path)) return false;

            if (rule.Scope == Scopes.Folder && !string.IsNullOrEmpty(rule.Root))
            {
                return IsUnder(path, rule.Root);
            }

            return string.Equals(AppPaths.CleanPath(rule.Path), path, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string p = AppPaths.CleanPath(path);
            string r = AppPaths.CleanPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string InferRoot(string exePath)
        {
            string path = AppPaths.CleanPath(exePath);
            if (string.IsNullOrEmpty(path)) return "";
            string dir;
            try
            {
                dir = Path.GetDirectoryName(path);
            }
            catch
            {
                return "";
            }
            if (string.IsNullOrEmpty(dir)) return "";

            DirectoryInfo info = new DirectoryInfo(dir);
            for (int i = 0; i < 2; i++)
            {
                if (info.Parent == null) break;
                if (VersionFolder.IsMatch(info.Name) && !IsUnsafeRoot(info.Parent.FullName))
                {
                    info = info.Parent;
                }
                else
                {
                    break;
                }
            }

            if (IsUnsafeRoot(info.FullName)) return "";
            return info.FullName;
        }

        public static bool IsUnsafeRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string p = AppPaths.CleanPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(p)) return true;
            if (p.Length <= 3) return true;

            string[] blocked =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            for (int i = 0; i < blocked.Length; i++)
            {
                if (string.IsNullOrEmpty(blocked[i])) continue;
                if (string.Equals(p, AppPaths.CleanPath(blocked[i]).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<string> Targets(AppRule rule)
        {
            List<string> result = new List<string>();
            if (rule == null) return result;

            string exe = AppPaths.CleanPath(rule.Path);
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(exe);
            }

            if (rule.Scope == Scopes.Folder && !string.IsNullOrEmpty(rule.Root) && !IsUnsafeRoot(rule.Root))
            {
                foreach (string file in EnumerateExecutables(rule.Root, 2, 80))
                {
                    if (!result.Any(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(file);
                    }
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static IEnumerable<string> EnumerateExecutables(string root, int maxDepth, int maxFiles)
        {
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return files;

            Stack<KeyValuePair<string, int>> stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0 && files.Count < maxFiles)
            {
                KeyValuePair<string, int> item = stack.Pop();
                string dir = item.Key;
                int depth = item.Value;
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*.exe"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (Uninstaller.IsMatch(name)) continue;
                        files.Add(file);
                        if (files.Count >= maxFiles) break;
                    }
                    if (depth < maxDepth)
                    {
                        foreach (string sub in Directory.GetDirectories(dir))
                        {
                            DirectoryInfo info = new DirectoryInfo(sub);
                            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (string.Equals(info.Name, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.Equals(info.Name, "locales", StringComparison.OrdinalIgnoreCase)) continue;
                            stack.Push(new KeyValuePair<string, int>(sub, depth + 1));
                        }
                    }
                }
                catch
                {
                }
            }
            return files;
        }

        public static bool IsChromiumApp(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            string name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            if (name == "claude" || name == "chatgpt" || name == "cursor" || name == "windsurf" ||
                name == "code" || name == "discord" || name == "slack")
            {
                return true;
            }

            try
            {
                string dir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(dir)) return false;
                if (File.Exists(Path.Combine(dir, "resources", "app.asar"))) return true;
                if (File.Exists(Path.Combine(dir, "chrome_elf.dll"))) return true;
                if (File.Exists(Path.Combine(dir, "libEGL.dll"))) return true;
                if (Directory.GetFiles(dir, "*.pak").Length > 0) return true;
            }
            catch
            {
            }
            return false;
        }

        public static string DescribeRule(AppRule rule)
        {
            if (rule == null) return "";
            return rule.Name + " · " + NetworkModes.Label(rule.Network);
        }
    }
}
