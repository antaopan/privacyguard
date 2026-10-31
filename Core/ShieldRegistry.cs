using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyGuard
{
    public static class ShieldRegistry
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan PendingWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PidWindow = TimeSpan.FromMinutes(10);

        public static void Begin(string exePath)
        {
            lock (Sync)
            {
                ShieldPidStore store = StatusStore.LoadShieldPids();
                Prune(store);
                store.PendingPath = AppPaths.CleanPath(exePath);
                store.PendingAt = DateTime.UtcNow.ToString("o");
                StatusStore.SaveShieldPids(store);
            }
        }

        public static void Cancel(string exePath)
        {
            lock (Sync)
            {
                ShieldPidStore store = StatusStore.LoadShieldPids();
                if (PathsEqual(store.PendingPath, exePath))
                {
                    store.PendingPath = "";
                    store.PendingAt = "";
                    StatusStore.SaveShieldPids(store);
                }
            }
        }

        public static void Complete(int pid, string exePath)
        {
            lock (Sync)
            {
                ShieldPidStore store = StatusStore.LoadShieldPids();
                Prune(store);
                store.Pids.Add(new ShieldPidRecord
                {
                    Pid = pid,
                    Path = AppPaths.CleanPath(exePath),
                    Time = DateTime.UtcNow.ToString("o")
                });
                if (PathsEqual(store.PendingPath, exePath))
                {
                    store.PendingPath = "";
                    store.PendingAt = "";
                }
                StatusStore.SaveShieldPids(store);
            }
        }

        public static bool IsAllowed(int pid, string exePath)
        {
            lock (Sync)
            {
                ShieldPidStore store = StatusStore.LoadShieldPids();
                DateTime now = DateTime.UtcNow;
                bool allowed = false;

                for (int i = store.Pids.Count - 1; i >= 0; i--)
                {
                    ShieldPidRecord record = store.Pids[i];
                    if (record == null)
                    {
                        store.Pids.RemoveAt(i);
                        continue;
                    }
                    DateTime time;
                    if (!DateTime.TryParse(record.Time, out time)) time = DateTime.MinValue;
                    if (now - time > PidWindow)
                    {
                        store.Pids.RemoveAt(i);
                        continue;
                    }
                    if (record.Pid == pid && PathsEqual(record.Path, exePath)) allowed = true;
                }

                if (!allowed && !string.IsNullOrEmpty(store.PendingPath))
                {
                    DateTime pendingAt;
                    if (!DateTime.TryParse(store.PendingAt, out pendingAt)) pendingAt = DateTime.MinValue;
                    if (now - pendingAt <= PendingWindow && PathsEqual(store.PendingPath, exePath))
                    {
                        allowed = true;
                    }
                }

                if (!allowed && !string.IsNullOrEmpty(store.PendingPath))
                {
                    DateTime pendingAt;
                    if (!DateTime.TryParse(store.PendingAt, out pendingAt)) pendingAt = DateTime.MinValue;
                    if (now - pendingAt > PendingWindow)
                    {
                        store.PendingPath = "";
                        store.PendingAt = "";
                    }
                }

                return allowed;
            }
        }

        private static void Prune(ShieldPidStore store)
        {
            if (store == null || store.Pids == null) return;
            DateTime now = DateTime.UtcNow;
            for (int i = store.Pids.Count - 1; i >= 0; i--)
            {
                ShieldPidRecord record = store.Pids[i];
                if (record == null)
                {
                    store.Pids.RemoveAt(i);
                    continue;
                }
                DateTime time;
                if (!DateTime.TryParse(record.Time, out time)) time = DateTime.MinValue;
                if (now - time > PidWindow) store.Pids.RemoveAt(i);
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(AppPaths.CleanPath(a), AppPaths.CleanPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
