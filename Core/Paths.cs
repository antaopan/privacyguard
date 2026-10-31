using System;
using System.IO;
using System.Reflection;

namespace PrivacyGuard
{
    public static class AppPaths
    {
        private static readonly object Sync = new object();
        private static string _dataDir;

        public static string ExePath
        {
            get
            {
                try
                {
                    Assembly asm = Assembly.GetEntryAssembly();
                    if (asm != null && !string.IsNullOrEmpty(asm.Location))
                    {
                        return Path.GetFullPath(asm.Location);
                    }
                }
                catch
                {
                }
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrivacyGuard.exe");
            }
        }

        public static string InstallDir
        {
            get { return Path.GetDirectoryName(ExePath); }
        }

        public static string DataDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_dataDir)) return _dataDir;
                lock (Sync)
                {
                    if (!string.IsNullOrEmpty(_dataDir)) return _dataDir;
                    string custom = Environment.GetEnvironmentVariable("PRIVACYGUARD_DATA");
                    if (!string.IsNullOrEmpty(custom))
                    {
                        _dataDir = Path.GetFullPath(custom);
                    }
                    else
                    {
                        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                        if (string.IsNullOrEmpty(common))
                        {
                            common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ProgramData");
                        }
                        _dataDir = Path.Combine(common, "PrivacyGuard");
                    }
                    return _dataDir;
                }
            }
        }

        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string StatusFile { get { return Path.Combine(DataDir, "status.json"); } }
        public static string CommandFile { get { return Path.Combine(DataDir, "command.json"); } }
        public static string StatsFile { get { return Path.Combine(DataDir, "stats.json"); } }
        public static string ShieldPidsFile { get { return Path.Combine(DataDir, "shield-pids.json"); } }
        public static string MachineBackupFile { get { return Path.Combine(DataDir, "backup-machine.json"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string WorkRoot { get { return Path.Combine(DataDir, "work"); } }
        public static string ConfigBackupDir { get { return Path.Combine(DataDir, "backups"); } }

        public static void EnsureDataDir()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(ConfigBackupDir);
            Directory.CreateDirectory(WorkRoot);
        }

        public static string LogFile(string component)
        {
            return Path.Combine(LogDir, string.Format("{0}-{1:yyyyMMdd}.log", component, DateTime.Now));
        }

        public static string CleanPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string value = path.Trim().Trim('"');
            try
            {
                if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return value;
                return Path.GetFullPath(value);
            }
            catch
            {
                return value;
            }
        }

        public static string SafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "app";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            string result = new string(chars).Trim();
            if (result.Length > 48) result = result.Substring(0, 48);
            if (result.Length == 0) result = "app";
            return result;
        }
    }
}
