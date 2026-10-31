using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PrivacyGuard
{
    public static class AppDiscovery
    {
        private class KnownApp
        {
            public string Name;
            public string[] Keywords;
            public string[] ExeNames;
            public string[] RelativePaths;
        }

        private static List<KnownApp> Known()
        {
            List<KnownApp> list = new List<KnownApp>();
            Add(list, "Claude Desktop", new[] { "anthropic", "claude" }, new[] { "claude.exe" },
                new[] { @"AnthropicClaude", @"Programs\Claude", @"Programs\AnthropicClaude" });
            Add(list, "ChatGPT", new[] { "chatgpt", "openai" }, new[] { "chatgpt.exe" },
                new[] { @"Programs\ChatGPT", @"ChatGPT" });
            Add(list, "Cursor", new[] { "cursor" }, new[] { "cursor.exe" },
                new[] { @"Programs\cursor", @"Programs\Cursor" });
            Add(list, "Windsurf", new[] { "windsurf", "codeium" }, new[] { "windsurf.exe" },
                new[] { @"Programs\Windsurf" });
            Add(list, "Perplexity", new[] { "perplexity" }, new[] { "perplexity.exe" },
                new[] { @"Programs\Perplexity" });
            Add(list, "Gemini", new[] { "gemini", "google gemini" }, new[] { "gemini.exe" },
                new[] { @"Programs\Gemini" });
            Add(list, "Poe", new[] { "poe" }, new[] { "poe.exe" },
                new[] { @"Programs\Poe" });
            Add(list, "Ollama", new[] { "ollama" }, new[] { "ollama app.exe", "ollama.exe" },
                new[] { @"Programs\Ollama", @"Ollama" });
            Add(list, "LM Studio", new[] { "lm studio", "lmstudio" }, new[] { "lm studio.exe", "lmstudio.exe" },
                new[] { @"LM-Studio", @"Programs\LM Studio" });
            Add(list, "Jan", new[] { "jan.ai", "jan " }, new[] { "jan.exe" },
                new[] { @"Programs\Jan", @"Jan" });
            Add(list, "Msty", new[] { "msty" }, new[] { "msty.exe" },
                new[] { @"Programs\Msty" });
            Add(list, "AnythingLLM", new[] { "anythingllm" }, new[] { "anythingllm.exe", "anythingllm desktop.exe" },
                new[] { @"Programs\AnythingLLM" });
            Add(list, "Chatbox", new[] { "chatbox" }, new[] { "chatbox.exe" },
                new[] { @"Programs\Chatbox" });
            Add(list, "DeepSeek", new[] { "deepseek" }, new[] { "deepseek.exe" },
                new[] { @"Programs\DeepSeek" });
            Add(list, "Qwen / 通义千问", new[] { "qwen", "通义" }, new[] { "qwen.exe" },
                new[] { @"Programs\Qwen", @"Qwen" });
            Add(list, "Kimi", new[] { "kimi", "月之暗面" }, new[] { "kimi.exe" },
                new[] { @"Programs\Kimi", @"Kimi" });
            Add(list, "豆包", new[] { "doubao", "豆包" }, new[] { "doubao.exe" },
                new[] { @"Programs\Doubao", @"Doubao" });
            Add(list, "腾讯元宝", new[] { "yuanbao", "元宝" }, new[] { "yuanbao.exe" },
                new[] { @"Programs\Yuanbao", @"Yuanbao" });
            Add(list, "智谱清言", new[] { "chatglm", "zhipu", "智谱" }, new[] { "chatglm.exe" },
                new[] { @"Programs\ChatGLM", @"ChatGLM" });
            Add(list, "文心一言", new[] { "wenxin", "文心" }, new[] { "wenxin.exe" },
                new[] { @"Programs\Wenxin", @"Wenxin" });
            return list;
        }

        private static void Add(List<KnownApp> list, string name, string[] keywords, string[] exeNames, string[] relativePaths)
        {
            KnownApp app = new KnownApp();
            app.Name = name;
            app.Keywords = keywords;
            app.ExeNames = exeNames;
            app.RelativePaths = relativePaths;
            list.Add(app);
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
                    string basePath = i == 0 || app.RelativePaths[i].StartsWith("Programs\\", StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(local, app.RelativePaths[i])
                        : Path.Combine(local, app.RelativePaths[i]);
                    TryAddDirectory(result, app, basePath);
                    TryAddDirectory(result, app, Path.Combine(programs, Path.GetFileName(app.RelativePaths[i])));
                    if (!string.IsNullOrEmpty(programsX86))
                    {
                        TryAddDirectory(result, app, Path.Combine(programsX86, Path.GetFileName(app.RelativePaths[i])));
                    }
                }
            }

            ScanUninstall(result, known, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanUninstall(result, known, RegistryHive.LocalMachine, RegistryView.Registry32);
            ScanUninstall(result, known, RegistryHive.CurrentUser, RegistryView.Registry64);
            ScanUninstall(result, known, RegistryHive.CurrentUser, RegistryView.Registry32);

            List<DetectedApp> distinct = new List<DetectedApp>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DetectedApp app in result)
            {
                string path = AppPaths.CleanPath(app.ExePath);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
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
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        string processName = process.ProcessName;
                        if (IsBrowserProcess(processName)) continue;
                        KnownApp match = FindMatch(known, processName);
                        if (match == null) match = FindMatch(known, process.MainWindowTitle);
                        if (match == null) continue;
                        string path = process.MainModule.FileName;
                        if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                        DetectedApp app = new DetectedApp();
                        app.Name = match.Name;
                        app.ExePath = AppPaths.CleanPath(path);
                        app.Source = "运行中的程序";
                        app.Installed = true;
                        result.Add(app);
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

        private static void TryAddDirectory(List<DetectedApp> result, KnownApp known, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string exe = FindExecutable(dir, known, 0);
            if (string.IsNullOrEmpty(exe)) return;
            DetectedApp app = new DetectedApp();
            app.Name = known.Name;
            app.ExePath = exe;
            app.Source = "常见安装目录";
            app.Installed = true;
            result.Add(app);
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
                            DetectedApp app = new DetectedApp();
                            app.Name = match.Name;
                            app.ExePath = exe;
                            app.Source = "已安装程序";
                            app.Installed = true;
                            result.Add(app);
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

        private static KnownApp FindMatch(List<KnownApp> known, string display)
        {
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
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("update", StringComparison.OrdinalIgnoreCase)) continue;
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

