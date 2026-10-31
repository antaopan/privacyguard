using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PrivacyGuard
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;
        private ICollectionView _appsView;
        private ClipboardGuard _clipboard;
        private Forms.NotifyIcon _tray;
        private Forms.ToolStripMenuItem _trayToggle;
        private DispatcherTimer _statusTimer;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _logTimer;
        private DispatcherTimer _toastTimer;
        private DispatcherTimer _startupTimer;
        private bool _loading = true;
        private bool _reallyExit;
        private string _lastSavedHash = "";
        private bool _startMinimized;
        private bool _balloonShown;
        private bool _logsLoading;
        private bool _syncingMaster;
        private long _pendingRevision;
        private int _applySeq;
        private DateTime _lastServiceProbe = DateTime.MinValue;
        private bool _serviceProbeExists;
        private bool _serviceProbeRunning;
        private string _lastBlockedNotified = "";
        private AppRule _browseRule;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            DataContext = _vm;

            try
            {
                AppConfig config = ConfigStore.CreateDefaultIfMissing();
                _vm.Load(config);
                _lastSavedHash = ConfigSignature(config);
            }
            catch (Exception ex)
            {
                UiLog.Error("LOAD_CONFIG", ex.ToString());
                _vm.Load(new AppConfig());
                _lastSavedHash = ConfigSignature(_vm.BuildConfig());
                ShowToast("配置读取失败，已使用默认配置");
            }

            _appsView = CollectionViewSource.GetDefaultView(_vm.Apps);
            _appsView.Filter = FilterApp;
            AppsList.ItemsSource = _appsView;
            _vm.Apps.CollectionChanged += Apps_CollectionChanged;
            foreach (AppItem item in _vm.Apps) AttachApp(item);
            foreach (HardeningItem item in _vm.Hardening) item.PropertyChanged += Hardening_PropertyChanged;

            MasterToggle.IsChecked = _vm.ProtectionEnabled;
            if (_vm.Apps.Count > 0) LaunchAppCombo.SelectedIndex = 0;

            _clipboard = new ClipboardGuard(Dispatcher, GetActiveRules, ClipboardAlert);
            _clipboard.Enabled = _vm.Ui.ClipboardGuard && _vm.ProtectionEnabled;
            _clipboard.Start();

            _saveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(650);
            _saveTimer.Tick += SaveTimer_Tick;

            _statusTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _statusTimer.Interval = TimeSpan.FromSeconds(2);
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();

            _logTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _logTimer.Interval = TimeSpan.FromSeconds(6);
            _logTimer.Tick += LogTimer_Tick;
            _logTimer.Start();

            _toastTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _toastTimer.Interval = TimeSpan.FromSeconds(4);
            _toastTimer.Tick += ToastTimer_Tick;

            _startupTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            _startupTimer.Interval = TimeSpan.FromSeconds(1);
            _startupTimer.Tick += StartupTimer_Tick;

            CreateTray();
            _loading = false;

            SetNav(0);
            _vm.SetPage(0);
            RefreshStatus();
            LoadLogsAsync();
        }

        public bool StartMinimized
        {
            get { return _startMinimized; }
            set
            {
                _startMinimized = value;
                if (value)
                {
                    Dispatcher.BeginInvoke(new Action(HideToTrayOnStartup));
                }
            }
        }

        private void HideToTrayOnStartup()
        {
            ShowInTaskbar = false;
            if (IsVisible) Hide();
            _startupTimer.Start();
        }

        private void StartupTimer_Tick(object sender, EventArgs e)
        {
            _startupTimer.Stop();
            if (_vm.Ui.NotifyOnBlock)
            {
                ShowBalloon("隐私卫士正在保护", "后台服务已接管网络隔离与 AI 隐私加固。双击托盘图标打开控制台。");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (DashboardScroll != null) DashboardScroll.ScrollToTop();
            }), DispatcherPriority.Background);
        }

        public void OpenPage(int index)
        {
            if (index < 0 || index > 5) return;
            Navigate(index);
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int corner = 2;
                NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
            }
            catch
            {
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (MaxButton == null) return;
            MaxButton.Content = WindowState == WindowState.Maximized ? "\u25A3" : "\u25A1";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is ButtonBase) return true;
                if (current is Window) return false;
                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_reallyExit) return;
            if (_vm != null && _vm.Ui != null && _vm.Ui.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                e.Cancel = true;
                ExitUi();
            }
        }

        private void HideToTray()
        {
            try
            {
                ShowInTaskbar = false;
                Hide();
                if (!_balloonShown)
                {
                    _balloonShown = true;
                    ShowBalloon("隐私卫士仍在运行", "关闭窗口不会停止保护。双击托盘图标可重新打开，右键可暂停或退出界面。");
                }
            }
            catch
            {
            }
        }

        public void ShowFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            }
            catch (Exception ex)
            {
                UiLog.Error("SHOW_FROM_TRAY_FAILED", ex.ToString());
            }
        }

        private void ExitUi()
        {
            _reallyExit = true;
            try
            {
                if (_clipboard != null) _clipboard.Stop();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
            }
            catch
            {
            }
            System.Windows.Application.Current.Shutdown();
        }

        private void CreateTray()
        {
            try
            {
                _tray = new Forms.NotifyIcon();
                _tray.Icon = LoadTrayIcon();
                _tray.Text = "隐私卫士 PrivacyGuard";
                _tray.Visible = true;

                Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
                menu.Items.Add("打开隐私卫士", null, delegate { ShowFromTray(); });
                _trayToggle = new Forms.ToolStripMenuItem("暂停防护", null, delegate { ToggleProtectionFromTray(); });
                menu.Items.Add(_trayToggle);
                menu.Items.Add("立即隔离黑名单应用", null, delegate { PanicAsync(); });
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("退出界面（后台继续保护）", null, delegate { ExitUi(); });
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += delegate { ShowFromTray(); };
                _tray.BalloonTipClicked += delegate { ShowFromTray(); };
            }
            catch (Exception ex)
            {
                UiLog.Warn("TRAY_FAILED", ex.Message);
            }
        }

        private Drawing.Icon LoadTrayIcon()
        {
            try
            {
                Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(AppPaths.ExePath);
                if (icon != null) return icon;
            }
            catch
            {
            }
            return System.Drawing.SystemIcons.Application;
        }

        private void ToggleProtectionFromTray()
        {
            if (_vm == null) return;
            SetProtection(!_vm.ProtectionEnabled);
        }

        private async void SetProtection(bool enabled)
        {
            if (enabled && !_vm.ServiceInstalled)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show(this,
                    "需要先安装后台服务，才能启用网络隔离与系统加固。\r\n\r\n现在安装吗？",
                    "需要后台服务", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
                await InstallServiceAsync();
                return;
            }

            _vm.ProtectionEnabled = enabled;
            _syncingMaster = true;
            MasterToggle.IsChecked = enabled;
            _syncingMaster = false;
            _vm.UpdateChecklist();
            _vm.RefreshRisk();
            _clipboard.Enabled = enabled && _vm.Ui.ClipboardGuard;
            if (_trayToggle != null) _trayToggle.Text = enabled ? "暂停防护" : "启用防护";
            ScheduleSave();
            ShowToast(enabled ? "防护已启用，正在应用网络与隐私策略" : "防护已暂停，正在恢复系统设置");
        }

        private void MasterToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || _syncingMaster) return;
            SetProtection(MasterToggle.IsChecked == true);
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null) return;
            int index;
            if (!int.TryParse(Convert.ToString(button.Tag), out index)) return;
            Navigate(index);
        }

        private void Navigate(int index)
        {
            Tabs.SelectedIndex = index;
            SetNav(index);
            _vm.SetPage(index);

            if (index == 1)
            {
                if (_appsView != null) _appsView.Refresh();
                if (_vm.Apps.Count > 0 && LaunchAppCombo.SelectedIndex < 0) LaunchAppCombo.SelectedIndex = 0;
            }
            else if (index == 2)
            {
                if (LaunchAppCombo.SelectedIndex < 0 && _vm.Apps.Count > 0) LaunchAppCombo.SelectedIndex = 0;
            }
            else if (index == 4)
            {
                LoadLogsAsync();
            }
        }

        private void SetNav(int index)
        {
            ToggleButton[] buttons = { NavOverview, NavApps, NavLauncher, NavHardening, NavLogs, NavSettings };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null) buttons[i].IsChecked = i == index;
            }
        }

        private void GoApps_Click(object sender, RoutedEventArgs e)
        {
            Navigate(1);
        }

        private void GoLogs_Click(object sender, RoutedEventArgs e)
        {
            Navigate(4);
        }

        private async void RiskAction_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.ServiceInstalled)
            {
                await InstallServiceAsync();
            }
            else if (!_vm.ServiceOnline)
            {
                await StartServiceAsync();
            }
            else if (!_vm.FirewallEnabled)
            {
                OpenFirewallSettings();
            }
            else if (_vm.Apps.Count == 0)
            {
                Navigate(1);
                AddApp();
            }
            else
            {
                HardeningItem recall = _vm.Hardening.FirstOrDefault(h => h.Id == "recall");
                if (recall != null && !recall.IsEnabled)
                {
                    Navigate(3);
                }
                else if (!string.IsNullOrEmpty(_vm.LastError))
                {
                    System.Windows.MessageBox.Show(this, _vm.LastError, "最近错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OpenFirewallSettings()
        {
            try
            {
                Process.Start("windowsdefender://network");
            }
            catch
            {
                try { Process.Start("control.exe", "/name Microsoft.WindowsFirewall"); } catch { }
            }
        }

        private void ScheduleSave()
        {
            if (_loading) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private async void SaveTimer_Tick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            await SaveAndApplyAsync(true, false);
        }

        private async Task<bool> SaveAndApplyAsync(bool notify, bool force)
        {
            try
            {
                if (_vm.Ui == null) return false;
                AppConfig config = _vm.BuildConfig();
                string signature = ConfigSignature(config);
                if (!force && string.Equals(signature, _lastSavedHash, StringComparison.Ordinal))
                {
                    return true;
                }
                ConfigStore.Save(config);
                _lastSavedHash = signature;
                _pendingRevision = config.Revision;

                bool running = ServiceHelper.IsRunning();
                if (!running)
                {
                    _pendingRevision = 0;
                    _vm.RefreshRisk();
                    if (notify) ShowToast("配置已保存；后台服务未运行，联网隔离暂未生效");
                    return false;
                }

                ControlCommand command = ConfigStore.WriteCommand("apply");
                int seq = ++_applySeq;
                long revision = config.Revision;

                for (int i = 0; i < 55; i++)
                {
                    await Task.Delay(160);
                    if (seq != _applySeq) return false;
                    ServiceStatus status = StatusStore.Load();
                    if (status != null && status.LastCommandId == command.Id && status.ConfigRevision == revision)
                    {
                        _pendingRevision = 0;
                        bool online = ServiceHelper.IsRunning();
                        _vm.UpdateStatus(status, online, true, true);
                        SyncMaster();
                        _clipboard.Enabled = _vm.ProtectionEnabled && _vm.Ui.ClipboardGuard;
                        if (notify) ShowToast("策略已同步到后台服务");
                        return true;
                    }
                }

                _pendingRevision = 0;
                RefreshStatus();
                if (notify) ShowToast("已保存，但后台服务未在预期时间内确认；请检查服务状态");
                return false;
            }
            catch (Exception ex)
            {
                _pendingRevision = 0;
                UiLog.Error("SAVE_FAILED", ex.ToString());
                if (notify) ShowToast("保存失败：" + ex.Message);
                return false;
            }
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            RefreshStatus();
        }

        private void LogTimer_Tick(object sender, EventArgs e)
        {
            if (Tabs.SelectedIndex == 0 || Tabs.SelectedIndex == 4) LoadLogsAsync();
        }

        private void RefreshStatus()
        {
            try
            {
                ServiceStatus status = StatusStore.Load();
                DateTime now = DateTime.UtcNow;
                if ((now - _lastServiceProbe).TotalSeconds > 5)
                {
                    _lastServiceProbe = now;
                    _serviceProbeExists = ServiceHelper.Exists();
                    _serviceProbeRunning = ServiceHelper.IsRunning();
                }

                bool online = status != null && status.ServiceRunning &&
                              StatusStore.AgeSeconds() < 12 && _serviceProbeRunning;
                bool updateProtection = _pendingRevision == 0 ||
                                        (status != null && status.ConfigRevision == _pendingRevision);

                _vm.UpdateStatus(status, online, _serviceProbeExists, updateProtection);
                SyncMaster();
                MasterToggle.IsEnabled = _vm.ServiceOnline;
                _clipboard.Enabled = _vm.ProtectionEnabled && _vm.Ui.ClipboardGuard && online;

                if (_trayToggle != null)
                {
                    _trayToggle.Text = _vm.ProtectionEnabled ? "暂停防护" : "启用防护";
                }

                NotifyNewBlock(status);
            }
            catch (Exception ex)
            {
                UiLog.Error("STATUS_FAILED", ex.ToString());
            }
        }

        private void SyncMaster()
        {
            _syncingMaster = true;
            MasterToggle.IsChecked = _vm.ProtectionEnabled;
            _syncingMaster = false;
        }

        private void NotifyNewBlock(ServiceStatus status)
        {
            if (status == null || !_vm.Ui.NotifyOnBlock || string.IsNullOrEmpty(status.LastBlockedAt)) return;
            if (string.Equals(_lastBlockedNotified, status.LastBlockedAt, StringComparison.Ordinal)) return;
            bool first = string.IsNullOrEmpty(_lastBlockedNotified);
            _lastBlockedNotified = status.LastBlockedAt;
            if (first) return;

            string text = status.LastBlockedName + " 已被检测到" + (status.ProtectionEnabled ? "" : "（防护已暂停）");
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                ShowToast(text);
            }
            else
            {
                ShowBalloon("黑名单应用活动", text);
            }
        }

        private void ClipboardAlert(string message)
        {
            if (IsVisible && WindowState != WindowState.Minimized) ShowToast(message);
            else ShowBalloon("剪贴板密钥守卫", message);
        }

        private List<AppRule> GetActiveRules()
        {
            if (_vm == null) return new List<AppRule>();
            return _vm.Apps.Where(a => a != null && a.Enabled).Select(a => a.ToRule()).ToList();
        }

        private bool FilterApp(object item)
        {
            AppItem app = item as AppItem;
            if (app == null) return false;
            string query = AppSearch == null ? "" : (AppSearch.Text ?? "").Trim();
            if (query.Length == 0) return true;
            return (app.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (app.ShortPath ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AppSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_appsView != null) _appsView.Refresh();
        }

        private void Apps_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (AppItem item in e.NewItems) AttachApp(item);
            }
            if (e.OldItems != null)
            {
                foreach (AppItem item in e.OldItems) item.PropertyChanged -= App_PropertyChanged;
            }
        }

        private void AttachApp(AppItem item)
        {
            if (item == null) return;
            item.PropertyChanged -= App_PropertyChanged;
            item.PropertyChanged += App_PropertyChanged;
        }

        private void App_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_loading) return;
            if (e.PropertyName == "Enabled" || e.PropertyName == "Network" || e.PropertyName == "Shield" || e.PropertyName == "Kill")
            {
                _vm.RefreshRisk();
                ScheduleSave();
            }
        }

        private void Hardening_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_loading) return;
            if (e.PropertyName == "IsEnabled")
            {
                _vm.NotifyHardeningChanged();
                ScheduleSave();
            }
        }

        private void RuleSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _vm.RefreshRisk();
            ScheduleSave();
        }

        private void HardeningToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _vm.NotifyHardeningChanged();
            ScheduleSave();
        }

        private void ApplyRecommended_Click(object sender, RoutedEventArgs e)
        {
            _vm.ApplyRecommendedHardening();
            _vm.NotifyHardeningChanged();
            ScheduleSave();
            ShowToast("已选择推荐加固项，正在应用");
        }

        private void DeleteApp_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            AppItem item = button == null ? null : button.Tag as AppItem;
            if (item == null) return;
            MessageBoxResult result = System.Windows.MessageBox.Show(this,
                "从黑名单移除“" + item.Name + "”？\r\n\r\n对应的防火墙规则会被删除。",
                "移除应用", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _vm.RemoveApp(item);
            ScheduleSave();
            ShowToast("已移除 " + item.Name);
        }

        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            AddApp();
        }

        private void AddApp()
        {
            AddAppWindow dialog = new AddAppWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _vm.AddApp(dialog.Result);
                _appsView.Refresh();
                ScheduleSave();
                ShowToast("已添加 " + dialog.Result.Name);
            }
        }

        private async void ScanApps_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true, "正在扫描已安装的 AI 应用...");
                List<DetectedApp> found = await Task.Run(delegate { return AppDiscovery.Detect(); });
                SetBusy(false, "");

                List<DetectedApp> filtered = found.Where(d => !_vm.ContainsPath(d.ExePath)).ToList();
                if (filtered.Count == 0)
                {
                    System.Windows.MessageBox.Show(this,
                        found.Count == 0
                            ? "没有扫描到常见 AI 桌面应用。\r\n\r\n可以手动点击“添加程序”选择 exe 文件。"
                            : "扫描到的应用都已在黑名单中。",
                        "扫描完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ScanWindow dialog = new ScanWindow(filtered);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true && dialog.Results.Count > 0)
                {
                    foreach (AppRule rule in dialog.Results) _vm.AddApp(rule);
                    _appsView.Refresh();
                    ScheduleSave();
                    ShowToast("已添加 " + dialog.Results.Count + " 个应用");
                }
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("SCAN_FAILED", ex.ToString());
                System.Windows.MessageBox.Show(this, "扫描失败：" + ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LauncherSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _vm.NotifyLauncherChanged();
            ScheduleSave();
        }

        private void LauncherSetting_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            _vm.NotifyLauncherChanged();
            ScheduleSave();
        }

        private void LaunchApp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LaunchAppCombo.SelectedItem != null) _browseRule = null;
        }

        private void BrowseLaunchApp_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择要以隐私模式启动的程序";
            dialog.Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            dialog.CheckFileExists = true;
            if (dialog.ShowDialog(this) != true) return;

            string path = AppPaths.CleanPath(dialog.FileName);
            AppItem existing = _vm.Apps.FirstOrDefault(a => string.Equals(AppPaths.CleanPath(a.Path), path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                LaunchAppCombo.SelectedItem = existing;
                _browseRule = null;
                return;
            }

            AppRule rule = new AppRule();
            rule.Name = Path.GetFileNameWithoutExtension(path);
            rule.Path = path;
            rule.Scope = Scopes.File;
            rule.Network = NetworkModes.Full;
            rule.Shield = true;
            rule.Enabled = true;
            _vm.AddApp(rule);
            _appsView.Refresh();
            ScheduleSave();
            LaunchAppCombo.SelectedItem = _vm.Apps[_vm.Apps.Count - 1];
            _browseRule = rule;
            ShowToast("已加入黑名单并以该程序作为启动目标");
        }

        private async void LaunchShield_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppItem item = LaunchAppCombo.SelectedItem as AppItem;
                AppRule rule = item != null ? item.ToRule() : _browseRule;
                if (rule == null || !File.Exists(rule.Path))
                {
                    System.Windows.MessageBox.Show(this, "请选择黑名单中的程序，或浏览一个存在的 exe 文件。",
                        "没有可启动的程序", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!_vm.ServiceOnline)
                {
                    MessageBoxResult proceed = System.Windows.MessageBox.Show(this,
                        "后台服务未运行，网络隔离不会生效。\r\n\r\n仍要以隐私模式启动吗？",
                        "服务未运行", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) return;
                }
                else
                {
                    await SaveAndApplyAsync(false, false);
                }

                LauncherConfig launcher = _vm.Launcher.Clone();
                SetBusy(true, "正在以隐私模式启动...");
                ShieldLaunchResult result = await Task.Run(delegate
                {
                    return ShieldLauncher.Launch(rule, launcher, "");
                });
                SetBusy(false, "");

                if (!result.Success)
                {
                    System.Windows.MessageBox.Show(this, result.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ShowToast("已启动 " + rule.Name + " · PID " + result.Pid);
                LoadLogsAsync();
                if (result.ElevatedToken)
                {
                    System.Windows.MessageBox.Show(this,
                        "程序已启动，但当前 Explorer 令牌带有管理员权限（通常因为 UAC 被关闭）。\r\n\r\n" +
                        "这会让程序继承管理员权限，降低隔离效果。建议启用 UAC 或使用标准用户账户。",
                        "权限提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("LAUNCH_FAILED", ex.ToString());
                System.Windows.MessageBox.Show(this, "启动失败：" + ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UiSetting_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _clipboard.Enabled = _vm.Ui.ClipboardGuard && _vm.ProtectionEnabled;
            ScheduleSave();
        }

        private async void InstallService_Click(object sender, RoutedEventArgs e)
        {
            await InstallServiceAsync();
        }

        private async Task InstallServiceAsync()
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(this,
                "将安装 PrivacyGuard 后台服务、开机自启，并应用当前配置。\r\n\r\n继续吗？",
                "安装 / 修复后台服务", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                ConfigStore.Save(_vm.BuildConfig());
                SetBusy(true, "正在安装后台服务...");
                InstallResult result = await Task.Run(delegate { return Installer.Install(true); });
                SetBusy(false, "");
                if (!result.Success)
                {
                    System.Windows.MessageBox.Show(this, result.Message, "安装未完成", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _vm.ServiceInstalled = true;
                _lastServiceProbe = DateTime.MinValue;
                RefreshStatus();
                LoadLogsAsync();
                string text = result.Message;
                if (!string.IsNullOrEmpty(result.Warning)) text += "\r\n\r\n" + result.Warning;
                if (!Installer.IsInstalledFromProgramFiles())
                {
                    text += "\r\n\r\n提示：当前程序不在 Program Files 中。正式使用时请通过安装包安装到 Program Files，避免移动目录后服务路径失效。";
                }
                System.Windows.MessageBox.Show(this, text, "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("INSTALL_FAILED", ex.ToString());
                System.Windows.MessageBox.Show(this, "安装失败：" + ex.Message, "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartService_Click(object sender, RoutedEventArgs e)
        {
            await StartServiceAsync();
        }

        private async Task StartServiceAsync()
        {
            try
            {
                SetBusy(true, "正在启动后台服务...");
                string error = "";
                bool ok = await Task.Run(delegate { return ServiceHelper.Start(out error); });
                SetBusy(false, "");
                if (!ok)
                {
                    System.Windows.MessageBox.Show(this, "启动失败：" + error, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _lastServiceProbe = DateTime.MinValue;
                RefreshStatus();
                LoadLogsAsync();
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("START_SERVICE_FAILED", ex.ToString());
            }
        }

        private async void StopService_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(this,
                "停止后台服务会立即移除防火墙规则并恢复系统加固设置。\r\n\r\n继续吗？",
                "停止后台服务", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                SetBusy(true, "正在停止后台服务...");
                string error = "";
                bool ok = await Task.Run(delegate { return ServiceHelper.Stop(out error); });
                SetBusy(false, "");
                if (!ok)
                {
                    System.Windows.MessageBox.Show(this, "停止失败：" + error, "停止失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _lastServiceProbe = DateTime.MinValue;
                RefreshStatus();
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("STOP_SERVICE_FAILED", ex.ToString());
            }
        }

        private async void Reapply_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await SaveAndApplyAsync(true, true);
            if (!ok) RefreshStatus();
        }

        private void RestorePolicy_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(this,
                "这会暂停防护并恢复安装前的系统隐私设置（Recall、Copilot、剪贴板等）。\r\n\r\n继续吗？",
                "恢复系统设置", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            _vm.ProtectionEnabled = false;
            MasterToggle.IsChecked = false;
            _vm.RefreshRisk();
            if (!_vm.ServiceOnline)
            {
                try
                {
                    string policyError;
                    PolicyManager.RestoreAll(out policyError);
                    FirewallManager.RemoveAll();
                    if (!string.IsNullOrEmpty(policyError)) ShowToast("部分系统设置恢复失败：" + policyError);
                }
                catch (Exception ex)
                {
                    UiLog.Warn("DIRECT_RESTORE_FAILED", ex.Message);
                }
            }
            ScheduleSave();
            ShowToast("正在暂停防护并恢复系统设置");
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(this,
                "将移除后台服务、登录自启动、防火墙规则，并恢复系统隐私设置。\r\n\r\n程序文件与日志不会被自动删除。继续吗？",
                "卸载后台服务", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                SetBusy(true, "正在卸载后台服务...");
                InstallResult result = Installer.Uninstall();
                SetBusy(false, "");
                _lastServiceProbe = DateTime.MinValue;
                RefreshStatus();
                System.Windows.MessageBox.Show(this,
                    result.Message + (string.IsNullOrEmpty(result.Warning) ? "" : "\r\n\r\n" + result.Warning),
                    result.Success ? "卸载完成" : "卸载未完成",
                    MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                UiLog.Error("UNINSTALL_FAILED", ex.ToString());
                System.Windows.MessageBox.Show(this, "卸载失败：" + ex.Message, "卸载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(AppPaths.DataDir);
        }

        private void OpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(AppPaths.LogDir);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                UiLog.Warn("OPEN_FOLDER_FAILED", ex.Message);
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出 PrivacyGuard 配置";
            dialog.Filter = "JSON 文件 (*.json)|*.json";
            dialog.FileName = "privacyguard-config.json";
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ConfigStore.Save(_vm.BuildConfig());
                File.Copy(AppPaths.ConfigFile, dialog.FileName, true);
                ShowToast("配置已导出");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) LoadLogsAsync();
        }

        private void LogSearch_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_loading) LoadLogsAsync();
        }

        private void RefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadLogsAsync();
        }

        private async void LoadLogsAsync()
        {
            if (_logsLoading) return;
            _logsLoading = true;
            try
            {
                List<LogEntry> all = await Task.Run(delegate { return Logger.Tail(600); });
                string level = "全部";
                ComboBoxItem item = LogFilter.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null) level = Convert.ToString(item.Tag);
                string query = LogSearch == null ? "" : LogSearch.Text;
                _vm.RefreshLogs(all, level, query);
            }
            catch (Exception ex)
            {
                UiLog.Warn("LOGS_FAILED", ex.Message);
            }
            finally
            {
                _logsLoading = false;
            }
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出防护日志";
            dialog.Filter = "CSV 文件 (*.csv)|*.csv";
            dialog.FileName = "privacyguard-logs-" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("时间,级别,来源,事件,详情");
                foreach (LogEntry entry in Logger.Tail(5000))
                {
                    sb.Append(Csv(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(',');
                    sb.Append(Csv(entry.Level)).Append(',');
                    sb.Append(Csv(entry.Source)).Append(',');
                    sb.Append(Csv(entry.Event)).Append(',');
                    sb.AppendLine(Csv(entry.Detail));
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                ShowToast("日志已导出");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Csv(string value)
        {
            string text = value ?? "";
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        private void Panic_Click(object sender, RoutedEventArgs e)
        {
            PanicAsync();
        }

        private async void PanicAsync()
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(this,
                "将立即终止所有已启用的黑名单应用，并切换为完全防护。\r\n\r\n继续吗？",
                "立即隔离", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            if (!_vm.ServiceOnline)
            {
                System.Windows.MessageBox.Show(this, "后台服务未运行，无法执行隔离。请先安装或启动服务。",
                    "服务未运行", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _vm.ProtectionEnabled = true;
                MasterToggle.IsChecked = true;
                AppConfig config = _vm.BuildConfig();
                ConfigStore.Save(config);
                _pendingRevision = config.Revision;
                ControlCommand command = ConfigStore.WriteCommand("panic");
                SetBusy(true, "正在隔离黑名单应用...");

                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(150);
                    ServiceStatus status = StatusStore.Load();
                    if (status != null && status.LastCommandId == command.Id)
                    {
                        _pendingRevision = 0;
                        _vm.UpdateStatus(status, true, true, true);
                        SyncMaster();
                        SetBusy(false, "");
                        ShowToast("隔离完成，已终止匹配的黑名单进程");
                        LoadLogsAsync();
                        return;
                    }
                }
                SetBusy(false, "");
                _pendingRevision = 0;
                RefreshStatus();
                ShowToast("隔离命令已发送，但服务未及时确认");
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                _pendingRevision = 0;
                UiLog.Error("PANIC_FAILED", ex.ToString());
                System.Windows.MessageBox.Show(this, ex.Message, "隔离失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _vm.IsBusy = busy;
            _vm.BusyText = text;
            BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        }

        private void ShowToast(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message)) return;
                ToastText.Text = message;
                ToastBorder.Visibility = Visibility.Visible;
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                _toastTimer.Stop();
                _toastTimer.Start();
            }
            catch
            {
            }
        }

        private void ToastTimer_Tick(object sender, EventArgs e)
        {
            _toastTimer.Stop();
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fadeOut.Completed += delegate
            {
                ToastBorder.Visibility = Visibility.Collapsed;
                ToastBorder.Opacity = 0;
            };
            ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private static string ConfigSignature(AppConfig config)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(config.Enabled ? '1' : '0').Append('\n');
            if (config.Apps != null)
            {
                foreach (AppRule app in config.Apps)
                {
                    if (app == null) continue;
                    sb.Append(app.Id).Append('|')
                      .Append(app.Path).Append('|')
                      .Append(app.Root).Append('|')
                      .Append(app.Scope).Append('|')
                      .Append(app.Network).Append('|')
                      .Append(app.Shield ? '1' : '0').Append('|')
                      .Append(app.Kill ? '1' : '0').Append('|')
                      .Append(app.Enabled ? '1' : '0').Append('\n');
                }
            }
            HardeningConfig h = config.Hardening ?? new HardeningConfig();
            sb.Append(h.DisableRecall ? '1' : '0')
              .Append(h.DisableCopilot ? '1' : '0')
              .Append(h.DisableClipboardCloud ? '1' : '0')
              .Append(h.DisableAdvertisingId ? '1' : '0')
              .Append(h.DisableActivityHistory ? '1' : '0')
              .Append(h.DisableTelemetry ? '1' : '0')
              .Append(h.DisableCloudSearch ? '1' : '0')
              .Append(h.DisableLocation ? '1' : '0')
              .Append(h.DisableCameraMic ? '1' : '0')
              .Append('\n');
            LauncherConfig l = config.Launcher ?? new LauncherConfig();
            sb.Append(l.TimeZone).Append('|').Append(l.Language).Append('|')
              .Append(l.StripSecrets ? '1' : '0')
              .Append(l.NeutralWorkingDir ? '1' : '0')
              .Append(l.ChromiumLang ? '1' : '0')
              .Append('|').Append(l.FakeComputerName).Append('|').Append(l.ExtraArgs).Append('\n');
            UiConfig u = config.Ui ?? new UiConfig();
            sb.Append(u.ClipboardGuard ? '1' : '0')
              .Append(u.CloseToTray ? '1' : '0')
              .Append(u.NotifyOnBlock ? '1' : '0');
            using (SHA1 sha = SHA1.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                if (_tray == null) return;
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(4000);
            }
            catch
            {
            }
        }
    }
}


