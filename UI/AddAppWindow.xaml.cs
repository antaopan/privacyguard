using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace PrivacyGuard
{
    public partial class AddAppWindow : Window
    {
        public AppRule Result { get; private set; }

        public AddAppWindow()
        {
            InitializeComponent();
        }

        public AddAppWindow(string initialPath)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(initialPath))
            {
                PathBox.Text = initialPath;
                SetDefaultsForPath(initialPath);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择要加入黑名单的程序";
            dialog.Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            dialog.CheckFileExists = true;
            if (dialog.ShowDialog(this) != true) return;
            PathBox.Text = dialog.FileName;
            SetDefaultsForPath(dialog.FileName);
        }

        private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NameBox == null) return;
            string path = AppPaths.CleanPath(PathBox.Text);
            if (string.IsNullOrEmpty(NameBox.Text)) NameBox.Text = Path.GetFileNameWithoutExtension(path);
        }

        private void SetDefaultsForPath(string rawPath)
        {
            string path = AppPaths.CleanPath(rawPath);
            if (string.IsNullOrEmpty(path)) return;
            NameBox.Text = Path.GetFileNameWithoutExtension(path);
            string root = AppMatcher.InferRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                string dir = Path.GetDirectoryName(path);
                bool versioned = !string.Equals(root, dir, StringComparison.OrdinalIgnoreCase);
                int exeCount = 0;
                try { exeCount = Directory.GetFiles(dir, "*.exe").Length; } catch { }
                ScopeCombo.SelectedIndex = (versioned || exeCount > 1) ? 1 : 0;
            }
            else
            {
                ScopeCombo.SelectedIndex = 0;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string path = AppPaths.CleanPath(PathBox.Text);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                System.Windows.MessageBox.Show(this, "请选择一个存在的 .exe 文件。", "路径无效",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show(this, "请选择 .exe 可执行文件。", "路径无效",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string scope = "file";
            ComboBoxItem scopeItem = ScopeCombo.SelectedItem as ComboBoxItem;
            if (scopeItem != null && scopeItem.Tag != null) scope = Convert.ToString(scopeItem.Tag);

            AppRule rule = new AppRule();
            rule.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? Path.GetFileNameWithoutExtension(path) : NameBox.Text.Trim();
            rule.Path = path;
            rule.Scope = scope;
            rule.Root = scope == Scopes.Folder ? AppMatcher.InferRoot(path) : "";
            if (scope == Scopes.Folder && string.IsNullOrEmpty(rule.Root))
            {
                System.Windows.MessageBox.Show(this, "无法推断安全的安装目录，请改用“仅此程序”。", "范围无效",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ComboBoxItem networkItem = NetworkCombo.SelectedItem as ComboBoxItem;
            rule.Network = networkItem != null && networkItem.Tag != null ? Convert.ToString(networkItem.Tag) : NetworkModes.Full;
            rule.Shield = ShieldCheck.IsChecked == true;
            rule.Kill = KillCheck.IsChecked == true;
            rule.Enabled = true;
            rule.Normalize();

            Result = rule;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
