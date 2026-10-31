using System;
using System.Collections.Generic;
using System.IO;

namespace PrivacyGuard
{
    public static class StatusStore
    {
        public static ServiceStatus Load()
        {
            try
            {
                return JsonFile.Read<ServiceStatus>(AppPaths.StatusFile);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(ServiceStatus status)
        {
            try
            {
                status.Timestamp = DateTime.UtcNow.ToString("o");
                JsonFile.Write(AppPaths.StatusFile, status);
            }
            catch
            {
            }
        }

        public static double AgeSeconds()
        {
            try
            {
                if (!File.Exists(AppPaths.StatusFile)) return double.MaxValue;
                return (DateTime.UtcNow - File.GetLastWriteTimeUtc(AppPaths.StatusFile)).TotalSeconds;
            }
            catch
            {
                return double.MaxValue;
            }
        }

        public static bool IsFresh
        {
            get { return AgeSeconds() < 15.0; }
        }

        public static StatsData LoadStats()
        {
            try
            {
                StatsData stats = JsonFile.Read<StatsData>(AppPaths.StatsFile);
                if (stats == null) stats = new StatsData();
                stats.RollIfNeeded();
                return stats;
            }
            catch
            {
                return new StatsData();
            }
        }

        public static void SaveStats(StatsData stats)
        {
            try
            {
                JsonFile.Write(AppPaths.StatsFile, stats);
            }
            catch
            {
            }
        }

        public static ShieldPidStore LoadShieldPids()
        {
            try
            {
                ShieldPidStore store = JsonFile.Read<ShieldPidStore>(AppPaths.ShieldPidsFile);
                if (store == null) store = new ShieldPidStore();
                if (store.Pids == null) store.Pids = new List<ShieldPidRecord>();
                return store;
            }
            catch
            {
                return new ShieldPidStore();
            }
        }

        public static void SaveShieldPids(ShieldPidStore store)
        {
            try
            {
                JsonFile.Write(AppPaths.ShieldPidsFile, store);
            }
            catch
            {
            }
        }
    }
}
