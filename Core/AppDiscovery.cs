using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PrivacyGuard
{
    public static class AppDiscovery
    {
        public static readonly string[] AutoManagedIds = { "claude", "gemini", "codex" };
        public static readonly string[] AutoManagedNames = { "Claude", "Gemini", "Codex" };

        private class KnownApp
        {
            public string Id;
            public string Name;
            public string[] Keywords;
            public string[] ExeNames;
            public string[] RelativePaths;
            public string[] AbsolutePaths;
        }

        /// <summary>
        /// 只自动发现这三类应用。其他应用必须由用户通过“添加程序”手动加入黑名单。
        /// </summary>
        private static List<KnownApp> Known()
        {
            List<KnownApp> list = new List<KnownApp>();
            Add(list, "claude", "Claude",
                new[] { "anthropic", "claude" },
                new[] { "claude.exe" },
                new[] { @"AnthropicClaude", @"Programs\Claude", @"Programs\AnthropicClaude" },
                new[] { @"%APPDATA%\npm\node_modules\@anthropic-ai\claude-code" });

            Add(list, "gemini", "Gemini",
                new[] { "gemini", "google gemini" },
                new[] { "gemini.exe" },
                new[] { @"Programs\Gemini", @"Google\Gemini", @"Gemini" },
                new[] { @"%LOCALAPPDATA%\Google\Gemini", @"%APPDATA%\npm\node_modules\@google\gemini-cli" });

            Add(list, "codex", "Codex",
                new[] { "codex", "openai codex" },
                new[] { "codex.exe" },
                new[] { @"Programs\Codex", @"OpenAI\Codex" },
                new[]
                {
                    @"%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe",
                    @"%LOCALAPPDATA%\OpenAI\Codex"
                });
            return list;
        }

        private static void Add(List<KnownApp> list, string id, string name, string[] keywords,
            string[] exeNames, string[] relativePaths, string[] absolutePaths)
        {
            KnownApp app = new KnownApp();
            app.Id = id;
            app.Name = name;
            app.Keywords = keywords;
            app.ExeNames = exeNames;
            app.RelativePaths = relativePaths;
            app.AbsolutePaths = absolutePaths;
            list.Add(app);
        }

        public static bool IsAutoManaged(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return false;
            for (int i = 0; i < AutoManagedIds.Length; i++)
            {
                if (string.Equals(nameOrId, AutoManagedIds[i], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nameOrId, AutoManagedNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static List<DetectedApp> Detect()
        {
            List<DetectedApp> result = new List<DetectedApp>();
            List<KnownApp> known = Known();
            ScanRunning(result, known);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (KnownApp app in known)
            {
                for (int i = 0; i < app.RelativePaths.Length; i++)
                {
                    TryAddDirectory(result, app, Path.Combine(local, app.RelativePaths[i]));
                    TryAddDirectory(result, app, Path.Combine(programs, Path.GetFileName(app.RelativePaths[i])));
                    if (!string.IsNullOrEmpty(programsX86))
                    {
                        TryAddDirectory(result, app, Path.Combine(programsX86, Path.GetFileName(app.RelativePaths[i])));
                    }
                }

                for (int i = 0; i < app.AbsolutePaths.Length; i++)
                {
                    string absolute = Environment.ExpandEnvironmentVariables(app.AbsolutePaths[i]);
                    if (File.Exists(absolute))
                    {
                        AddDetected(result, app, absolute, "常见安装位置");
                    }
                    else if (Directory.Exists(absolute))
                    {
                        TryAddDirectory(result, app, absolute);
                    }
                }
            }

            ScanUninstall(result, known, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanUninstall(result, known, RegistryHive.LocalMachine, RegistryView.Registry32);
            ScanUninstall(result, known, RegistryHive.CurrentUser, RegistryView.Registry64);
            ScanUninstall(result, known, RegistryHive.CurrentUser, RegistryView.Registry32);

            List<DetectedApp> distinct = new List<DetectedApp>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DetectedApp app in result)
            {
                string path = AppPaths.CleanPath(app.ExePath);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) continue;
                if (!string.IsNullOrEmpty(app.Id) && !seenKinds.Add(app.Id)) continue;
                app.ExePath = path;
                distinct.Add(app);
            }
            distinct.Sort(delegate(DetectedApp a, DetectedApp b)
            {
                int n = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                if (n != 0) return n;
                return string.Compare(a.ExePath, b.ExePath, StringComparison.OrdinalIgnoreCase);
            });
            return distinct;
        }

        private static void ScanRunning(List<DetectedApp> result, List<KnownApp> known)
        {
            try
            {
                foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        string processName = process.ProcessName;
                        if (IsBrowserProcess(processName)) continue;
                        KnownApp match = FindProcessMatch(known, processName);
                        if (match == null)
                        {
                            if (process.MainWindowHandle == IntPtr.Zero) continue;
                            match = FindMatch(known, process.MainWindowTitle);
                        }
                        if (match == null) continue;
                        string path = process.MainModule.FileName;
                        if (string.IsNullOrEmpty(path) || !File.Exists(path) || IsUninstaller(path)) continue;
                        AddDetected(result, match, path, "运行中的程序");
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddDetected(List<DetectedApp> result, KnownApp known, string exePath, string source)
        {
            DetectedApp app = new DetectedApp();
            app.Id = known.Id;
            app.Name = known.Name;
            app.ExePath = AppPaths.CleanPath(exePath);
            app.Source = source;
            app.Installed = true;
            result.Add(app);
        }

        private static void TryAddDirectory(List<DetectedApp> result, KnownApp known, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string exe = FindExecutable(dir, known, 0);
            if (string.IsNullOrEmpty(exe)) return;
            AddDetected(result, known, exe, "常见安装目录");
        }

        private static void ScanUninstall(List<DetectedApp> result, List<KnownApp> known, RegistryHive hive, RegistryView view)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) return;
                    foreach (string subName in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey sub = uninstall.OpenSubKey(subName))
                        {
                            if (sub == null) continue;
                            string display = Convert.ToString(sub.GetValue("DisplayName"));
                            if (string.IsNullOrEmpty(display)) continue;
                            KnownApp match = FindMatch(known, display);
                            if (match == null) continue;

                            string exe = ResolveFromRegistry(sub, match);
                            if (string.IsNullOrEmpty(exe)) continue;
                            AddDetected(result, match, exe, "已安装程序");
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsBrowserProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string[] browsers = { "msedge", "chrome", "firefox", "iexplore", "opera", "brave", "vivaldi", "safari" };
            for (int i = 0; i < browsers.Length; i++)
            {
                if (string.Equals(processName, browsers[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsUninstaller(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("setup", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("installer", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("update", StringComparison.OrdinalIgnoreCase);
        }

        private static KnownApp FindProcessMatch(List<KnownApp> known, string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;
            for (int i = 0; i < known.Count; i++)
            {
                if (string.Equals(processName, known[i].Id, StringComparison.OrdinalIgnoreCase)) return known[i];
                for (int e = 0; e < known[i].ExeNames.Length; e++)
                {
                    string name = Path.GetFileNameWithoutExtension(known[i].ExeNames[e]);
                    if (string.Equals(processName, name, StringComparison.OrdinalIgnoreCase)) return known[i];
                }
            }
            return null;
        }

        private static KnownApp FindMatch(List<KnownApp> known, string display)
        {
            if (string.IsNullOrEmpty(display)) return null;
            for (int i = 0; i < known.Count; i++)
            {
                for (int k = 0; k < known[i].Keywords.Length; k++)
                {
                    if (display.IndexOf(known[i].Keywords[k], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return known[i];
                    }
                }
            }
            return null;
        }

        private static string ResolveFromRegistry(RegistryKey sub, KnownApp known)
        {
            string icon = Convert.ToString(sub.GetValue("DisplayIcon"));
            string exe = ExtractExe(icon);
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe) && !IsUninstaller(exe)) return exe;

            string install = Convert.ToString(sub.GetValue("InstallLocation"));
            if (!string.IsNullOrEmpty(install))
            {
                install = AppPaths.CleanPath(install);
                if (Directory.Exists(install))
                {
                    string found = FindExecutable(install, known, 0);
                    if (!string.IsNullOrEmpty(found)) return found;
                }
            }

            string display = Convert.ToString(sub.GetValue("DisplayName"));
            if (!string.IsNullOrEmpty(display))
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] candidates =
                {
                    Path.Combine(local, "Programs", SanitizeFolder(display)),
                    Path.Combine(local, SanitizeFolder(display)),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), SanitizeFolder(display))
                };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (Directory.Exists(candidates[i]))
                    {
                        string found = FindExecutable(candidates[i], known, 0);
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                }
            }
            return null;
        }

        private static string ExtractExe(string displayIcon)
        {
            if (string.IsNullOrEmpty(displayIcon)) return null;
            string value = displayIcon.Trim();
            int comma = value.LastIndexOf(',');
            if (comma > 0 && comma > value.LastIndexOf('"')) value = value.Substring(0, comma);
            value = value.Trim().Trim('"');
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return AppPaths.CleanPath(value);
            return null;
        }

        private static string SanitizeFolder(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = ' ';
            }
            return new string(chars).Trim();
        }

        private static string FindExecutable(string dir, KnownApp known, int depth)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                string best = null;
                for (int e = 0; e < known.ExeNames.Length; e++)
                {
                    string wanted = known.ExeNames[e];
                    for (int f = 0; f < files.Length; f++)
                    {
                        if (string.Equals(Path.GetFileName(files[f]), wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return files[f];
                        }
                    }
                }

                foreach (string file in files)
                {
                    if (IsUninstaller(file)) continue;
                    if (best == null || new FileInfo(file).Length > new FileInfo(best).Length) best = file;
                }
                if (best != null) return best;

                if (depth < 3)
                {
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        string found = FindExecutable(sub, known, depth + 1);
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }
}

