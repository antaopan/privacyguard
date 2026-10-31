using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PrivacyGuard
{
    public static class JsonFile
    {
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(fs);
            }
        }

        public static string ReadText(string path)
        {
            if (!File.Exists(path)) return null;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        public static void Write<T>(string path, T value)
        {
            AppPaths.EnsureDataDir();
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                data = ms.ToArray();
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (FileStream fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null, true);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                try
                {
                    File.Copy(temp, path, true);
                }
                finally
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }
    }

    public static class ConfigStore
    {
        private static readonly object Sync = new object();

        public static AppConfig Load(out string error)
        {
            error = "";
            try
            {
                AppConfig config = JsonFile.Read<AppConfig>(AppPaths.ConfigFile);
                if (config == null) return null;
                config.Normalize();
                return config;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Error("config", "LOAD_FAILED", ex.ToString());
                try
                {
                    if (File.Exists(AppPaths.ConfigFile))
                    {
                        AppPaths.EnsureDataDir();
                        string backup = Path.Combine(AppPaths.ConfigBackupDir,
                            string.Format("config.corrupt-{0:yyyyMMdd-HHmmss}.json", DateTime.Now));
                        File.Copy(AppPaths.ConfigFile, backup, true);
                    }
                }
                catch
                {
                }
                return null;
            }
        }

        public static AppConfig LoadOrDefault()
        {
            string error;
            AppConfig config = Load(out error);
            if (config == null) config = new AppConfig();
            return config;
        }

        public static bool Exists
        {
            get { return File.Exists(AppPaths.ConfigFile); }
        }

        public static AppConfig CreateDefaultIfMissing()
        {
            lock (Sync)
            {
                if (File.Exists(AppPaths.ConfigFile))
                {
                    string error;
                    AppConfig existing = Load(out error);
                    if (existing != null) return existing;
                }
                AppConfig config = new AppConfig();
                config.Touch();
                Save(config);
                Logger.Info("config", "CREATED", "已创建默认配置");
                return config;
            }
        }

        public static void Save(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            lock (Sync)
            {
                config.Normalize();
                config.Touch();
                JsonFile.Write(AppPaths.ConfigFile, config);
            }
        }

        public static DateTime GetLastWriteTimeUtc()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return DateTime.MinValue;
                return File.GetLastWriteTimeUtc(AppPaths.ConfigFile);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public static ControlCommand LoadCommand(out string raw)
        {
            raw = null;
            try
            {
                if (!File.Exists(AppPaths.CommandFile)) return null;
                raw = JsonFile.ReadText(AppPaths.CommandFile);
                if (string.IsNullOrEmpty(raw)) return null;
                return JsonFile.Read<ControlCommand>(AppPaths.CommandFile);
            }
            catch
            {
                return null;
            }
        }

        public static ControlCommand WriteCommand(string command)
        {
            ControlCommand cmd = new ControlCommand();
            cmd.Command = command;
            JsonFile.Write(AppPaths.CommandFile, cmd);
            return cmd;
        }
    }
}
