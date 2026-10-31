using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PrivacyGuard;

internal static class Harness
{
    private static TcpListener _listener;
    private static volatile bool _listening;
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));

    private static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("PRIVACYGUARD_DATA", Path.Combine(Root, "Tools\\Harness\\data"));
        string mode = args.Length > 0 ? args[0] : "all";
        int failures = 0;
        try
        {
            if (mode == "firewall" || mode == "all") failures += FirewallTest();
            if (mode == "shield" || mode == "all") failures += ShieldTest();
            if (mode == "node" || mode == "all") failures += NodeTest();
            if (mode == "policy" || mode == "all") failures += PolicyTest();
            if (mode == "config" || mode == "all") failures += ConfigTest();
            if (mode == "discovery" || mode == "all") failures += DiscoveryTest();
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS_EXCEPTION " + ex);
            failures++;
        }
        finally
        {
            try { FirewallManager.RemoveAll(); } catch { }
            StopServer();
        }
        Console.WriteLine(failures == 0 ? "HARNESS_PASS" : "HARNESS_FAIL " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static int FirewallTest()
    {
        Console.WriteLine("== firewall test ==");
        string probe = Path.Combine(Root, "Tools\\Probe\\PrivacyGuard.Probe.exe");
        string testHost = "1.1.1.1";
        int testPort = 80;

        ProcessResult before = RunProbe(probe, testHost, testPort);
        Console.WriteLine("before: " + before.Output.Trim());
        if (before.ExitCode != 0)
        {
            Console.WriteLine("FAIL baseline cannot connect (needs outbound test endpoint)");
            return 1;
        }

        AppConfig config = new AppConfig();
        config.Enabled = true;
        config.Apps = new List<AppRule>();
        AppRule app = new AppRule();
        app.Name = "Probe";
        app.Path = probe;
        app.Scope = Scopes.File;
        app.Network = NetworkModes.Full;
        app.Enabled = true;
        config.Apps.Add(app);

        FirewallApplyResult applied = FirewallManager.Apply(FirewallManager.BuildPlan(config));
        Console.WriteLine("rules added=" + applied.Added + " total=" + applied.Total + " error=" + applied.Error);
        Thread.Sleep(700);

        ProcessResult blocked = RunProbe(probe, testHost, testPort);
        Console.WriteLine("blocked: " + blocked.Output.Trim());
        bool okBlocked = blocked.ExitCode != 0;

        FirewallManager.RemoveAll();
        Thread.Sleep(500);
        ProcessResult after = RunProbe(probe, testHost, testPort);
        Console.WriteLine("after: " + after.Output.Trim());
        bool okAfter = after.ExitCode == 0;

        Console.WriteLine("firewall_block=" + (okBlocked ? "PASS" : "FAIL") +
                          " cleanup_restore=" + (okAfter ? "PASS" : "FAIL"));
        return okBlocked && okAfter ? 0 : 1;
    }

    private static int ShieldTest()
    {
        Console.WriteLine("== shield test ==");
        string probe = Path.Combine(Root, "Tools\\Probe\\PrivacyGuard.Probe.exe");
        string outFile = Path.Combine(Path.GetTempPath(), "pg-shield-dump-" + Guid.NewGuid().ToString("N") + ".txt");
        if (File.Exists(outFile)) File.Delete(outFile);

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test-should-not-pass");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test-should-not-pass");

        AppRule app = new AppRule();
        app.Name = "Probe";
        app.Path = probe;
        LauncherConfig config = new LauncherConfig();
        config.FakeComputerName = "DESKTOP-PRIVACY";
        config.TimeZone = "UTC";
        config.Language = "en-US";
        config.StripSecrets = true;
        config.NeutralWorkingDir = true;

        ShieldLaunchResult result = ShieldLauncher.Launch(app, config, "--dump \"" + outFile + "\"");
        Console.WriteLine("launch=" + result.Success + " pid=" + result.Pid + " msg=" + result.Message);
        if (!result.Success) return 1;

        for (int i = 0; i < 30 && !File.Exists(outFile); i++) Thread.Sleep(200);
        if (!File.Exists(outFile))
        {
            Console.WriteLine("FAIL no dump");
            return 1;
        }
        Thread.Sleep(300);
        string text = File.ReadAllText(outFile, Encoding.UTF8);
        Console.WriteLine(text);
        bool ok = text.Contains("env.TZ=UTC") &&
                  text.Contains("env.LANG=en_US.UTF-8") &&
                  text.Contains("env.COMPUTERNAME=DESKTOP-PRIVACY") &&
                  text.Contains("env.OPENAI_API_KEY.present=False") &&
                  text.Contains("env.ANTHROPIC_API_KEY.present=False") &&
                  text.Contains("env.PRIVACYGUARD_SHIELD=1");
        Console.WriteLine("shield=" + (ok ? "PASS" : "FAIL") + " elevated=" + text.Contains("elevated=True"));
        return ok ? 0 : 1;
    }

    private static int NodeTest()
    {
        Console.WriteLine("== node timezone test ==");
        string node = Environment.GetEnvironmentVariable("NODE_EXE");
        if (string.IsNullOrEmpty(node) || !File.Exists(node))
        {
            Console.WriteLine("SKIP no node path");
            return 1;
        }
        string script = Path.Combine(Root, "Tools\\Harness\\tz-check.js");
        string outFile = Path.Combine(Path.GetTempPath(), "pg-node-out-" + Guid.NewGuid().ToString("N") + ".json");
        if (File.Exists(outFile)) File.Delete(outFile);
        File.WriteAllText(script,
            "const fs=require('fs');fs.writeFileSync(process.argv[2],JSON.stringify({tz:process.env.TZ,intl:Intl.DateTimeFormat().resolvedOptions().timeZone,lang:process.env.LANG}));",
            new UTF8Encoding(false));

        AppRule app = new AppRule();
        app.Name = "Node";
        app.Path = node;
        LauncherConfig config = new LauncherConfig();
        config.TimeZone = "UTC";
        config.Language = "en-US";
        ShieldLaunchResult result = ShieldLauncher.Launch(app, config,
            "\"" + script + "\" \"" + outFile + "\"");
        Console.WriteLine("launch=" + result.Success + " msg=" + result.Message);
        if (!result.Success) return 1;
        for (int i = 0; i < 30 && !File.Exists(outFile); i++) Thread.Sleep(200);
        if (!File.Exists(outFile))
        {
            Console.WriteLine("FAIL no node output");
            return 1;
        }
        string text = File.ReadAllText(outFile, Encoding.UTF8);
        Console.WriteLine(text);
        bool ok = text.Contains("\"tz\":\"UTC\"") && text.Contains("\"intl\":\"UTC\"");
        Console.WriteLine("node_tz=" + (ok ? "PASS" : "FAIL"));
        return ok ? 0 : 1;
    }

    private static int PolicyTest()
    {
        Console.WriteLine("== policy test ==");
        const string keyPath = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";
        const string name = "DisabledByGroupPolicy";
        Microsoft.Win32.RegistryKey baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
            Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        object original;
        Microsoft.Win32.RegistryValueKind originalKind = Microsoft.Win32.RegistryValueKind.Unknown;
        using (Microsoft.Win32.RegistryKey key = baseKey.OpenSubKey(keyPath, false))
        {
            original = key == null ? null : key.GetValue(name, null);
            if (key != null && original != null) originalKind = key.GetValueKind(name);
        }

        try
        {
            HardeningConfig h = new HardeningConfig();
            h.SetRecommended();
            h.DisableRecall = false;
            h.DisableCopilot = false;
            h.DisableClipboardCloud = false;
            h.DisableAdvertisingId = true;
            h.DisableActivityHistory = false;
            h.DisableCloudSearch = false;
            h.DisableTelemetry = false;

            string error;
            int applied = PolicyManager.Apply(h, true, out error);
            Console.WriteLine("applied=" + applied + " error=" + error);

            object afterApply;
            using (Microsoft.Win32.RegistryKey key = baseKey.OpenSubKey(keyPath, false))
            {
                afterApply = key == null ? null : key.GetValue(name, null);
            }
            bool set = afterApply is int && (int)afterApply == 1;

            int restored = PolicyManager.RestoreAll(out error);
            Console.WriteLine("restored=" + restored + " error=" + error);
            object afterRestore;
            Microsoft.Win32.RegistryValueKind restoreKind = Microsoft.Win32.RegistryValueKind.Unknown;
            using (Microsoft.Win32.RegistryKey key = baseKey.OpenSubKey(keyPath, false))
            {
                afterRestore = key == null ? null : key.GetValue(name, null);
                if (key != null && afterRestore != null) restoreKind = key.GetValueKind(name);
            }
            bool restoredOk = ObjectsEqual(original, afterRestore) && originalKind == restoreKind;
            Console.WriteLine("policy_set=" + (set ? "PASS" : "FAIL") +
                              " policy_restore=" + (restoredOk ? "PASS" : "FAIL") +
                              " original=" + (original ?? "<null>") + " after=" + (afterRestore ?? "<null>"));
            return set && restoredOk ? 0 : 1;
        }
        finally
        {
            baseKey.Close();
        }
    }

    private static bool ObjectsEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.Ordinal);
    }

    private static int DiscoveryTest()
    {
        Console.WriteLine("== discovery test ==");
        List<DetectedApp> apps = AppDiscovery.Detect();
        foreach (DetectedApp app in apps.Take(20))
        {
            Console.WriteLine("FOUND " + app.Name + " | " + app.ExePath + " | " + app.Source);
        }
        Console.WriteLine("discovery=" + (apps.Count > 0 ? "PASS" : "WARN") + " count=" + apps.Count);
        return apps.Count > 0 ? 0 : 1;
    }

    private static int ConfigTest()
    {
        Console.WriteLine("== config test ==");
        AppConfig config = new AppConfig();
        AppRule app = new AppRule();
        app.Name = "Probe";
        app.Path = Path.Combine(Root, "Tools\\Probe\\PrivacyGuard.Probe.exe");
        app.Network = NetworkModes.Lan;
        app.Scope = Scopes.Folder;
        config.Apps.Add(app);
        config.Hardening.DisableTelemetry = true;
        ConfigStore.Save(config);

        string error;
        AppConfig loaded = ConfigStore.Load(out error);
        bool ok = loaded != null && loaded.Apps.Count == 1 &&
                  loaded.Apps[0].Network == NetworkModes.Lan &&
                  loaded.Apps[0].Scope == Scopes.Folder &&
                  loaded.Hardening.DisableTelemetry;
        Console.WriteLine("roundtrip=" + (ok ? "PASS" : "FAIL") + " error=" + error);
        return ok ? 0 : 1;
    }

    private class ProcessResult
    {
        public int ExitCode;
        public string Output;
    }

    private static ProcessResult RunProbe(string probe, string host, int port)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = probe;
        psi.Arguments = "--connect " + host + " --port " + port + " --timeout 2000";
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            ProcessResult result = new ProcessResult();
            result.ExitCode = p.ExitCode;
            result.Output = output;
            return result;
        }
    }

    private static string GetLanIp()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void StartServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _listening = true;
        Thread thread = new Thread(delegate()
        {
            while (_listening)
            {
                try
                {
                    if (!_listener.Pending()) { Thread.Sleep(50); continue; }
                    using (TcpClient client = _listener.AcceptTcpClient())
                    {
                        byte[] data = Encoding.UTF8.GetBytes("OK");
                        client.GetStream().Write(data, 0, data.Length);
                    }
                }
                catch
                {
                }
            }
        });
        thread.IsBackground = true;
        thread.Start();
        Console.WriteLine("server on " + port);
    }

    private static void StopServer()
    {
        _listening = false;
        try { if (_listener != null) _listener.Stop(); } catch { }
        _listener = null;
    }
}
