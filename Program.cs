using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Forms;

namespace PrivacyGuard
{
    internal static class Program
    {
        private static Mutex _instanceMutex;
        private static EventWaitHandle _showEvent;

        [STAThread]
        private static int Main(string[] args)
        {
            bool serviceMode = HasArg(args, "--service");
            if (serviceMode)
            {
                ServiceBase.Run(new PrivacyGuardService());
                return 0;
            }

            if (HasArg(args, "--install"))
            {
                InstallResult result = Installer.Install(true);
                if (!HasArg(args, "--quiet"))
                {
                    ShowInfo(result.Success ? "安装完成" : "安装未完成",
                        result.Message + (string.IsNullOrEmpty(result.Warning) ? "" : "\r\n\r\n" + result.Warning),
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                return result.Success ? 0 : 1;
            }

            if (HasArg(args, "--uninstall"))
            {
                InstallResult result = Installer.Uninstall();
                if (!HasArg(args, "--quiet"))
                {
                    ShowInfo(result.Success ? "卸载完成" : "卸载未完成",
                        result.Message + (string.IsNullOrEmpty(result.Warning) ? "" : "\r\n\r\n" + result.Warning),
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                return result.Success ? 0 : 1;
            }

            if (HasArg(args, "--repair"))
            {
                InstallResult result = Installer.Repair();
                if (!HasArg(args, "--quiet"))
                {
                    ShowInfo(result.Success ? "修复完成" : "修复未完成",
                        result.Message + (string.IsNullOrEmpty(result.Warning) ? "" : "\r\n\r\n" + result.Warning),
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                return result.Success ? 0 : 1;
            }

            bool created;
            _instanceMutex = new Mutex(true, "Global\\PrivacyGuard.UI", out created);
            if (!created)
            {
                try
                {
                    _showEvent = EventWaitHandle.OpenExisting("Global\\PrivacyGuard.Show");
                    _showEvent.Set();
                }
                catch
                {
                }
                return 0;
            }

            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\PrivacyGuard.Show");
            }
            catch
            {
                _showEvent = null;
            }

            try
            {
                System.Windows.Application app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

                ResourceDictionary theme = new ResourceDictionary();
                theme.Source = new Uri("pack://application:,,,/PrivacyGuard;component/UI/Theme.xaml", UriKind.Absolute);
                app.Resources.MergedDictionaries.Add(theme);

                MainWindow window = new MainWindow();
                window.StartMinimized = HasArg(args, "--minimized");
                if (!window.StartMinimized)
                {
                    window.Show();
                }
                int initialPage = GetIntArg(args, "--page", 0);
                if (initialPage > 0) window.OpenPage(initialPage);

                if (_showEvent != null)
                {
                    System.Windows.Threading.Dispatcher uiDispatcher = window.Dispatcher;
                    Thread listener = new Thread(delegate()
                    {
                        while (true)
                        {
                            try
                            {
                                _showEvent.WaitOne();
                                uiDispatcher.BeginInvoke(new Action(delegate()
                                {
                                    window.ShowFromTray();
                                }));
                            }
                            catch (Exception ex)
                            {
                                UiLog.Error("SHOW_EVENT_FAILED", ex.ToString());
                                return;
                            }
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                }

                return app.Run();
            }
            catch (Exception ex)
            {
                Logger.Error("ui", "START_FAILED", ex.ToString());
                ShowInfo("PrivacyGuard 无法启动", ex.Message, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int GetIntArg(string[] args, string name, int fallback)
        {
            if (args == null) return fallback;
            for (int i = 0; i < args.Length; i++)
            {
                string value = null;
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    value = args[i + 1];
                }
                else if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i].Substring(name.Length + 1);
                }
                if (value != null)
                {
                    int parsed;
                    if (int.TryParse(value, out parsed)) return parsed;
                }
            }
            return fallback;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void ShowInfo(string title, string message, MessageBoxIcon icon)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
            }
            catch
            {
            }
        }
    }
}
