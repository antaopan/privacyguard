using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrivacyGuard
{
    public class AppItem : INotifyPropertyChanged
    {
        private AppRule _rule;
        private bool _enabled;
        private string _network;
        private bool _shield;
        private bool _kill;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Root { get; set; }
        public string Scope { get; set; }
        public string ScopeText { get; set; }
        public string ShortPath { get; set; }
        public string StatusText { get; set; }
        public System.Windows.Media.Brush StatusBrush { get; set; }
        public ImageSource Icon { get; set; }
        public bool Exists { get; set; }

        public AppItem()
        {
            Name = "";
            Path = "";
            Root = "";
            Scope = Scopes.File;
            ScopeText = "仅此程序";
            ShortPath = "";
            StatusText = "";
            StatusBrush = System.Windows.Media.Brushes.Gray;
            _network = NetworkModes.Full;
            _enabled = true;
            _shield = true;
        }

        public static AppItem FromRule(AppRule rule)
        {
            AppItem item = new AppItem();
            item._rule = rule;
            item.Id = rule.Id;
            item.Name = rule.Name;
            item.Path = rule.Path;
            item.Root = rule.Root;
            item.Scope = rule.Scope;
            item.ScopeText = Scopes.Label(rule.Scope);
            item._network = rule.Network;
            item._enabled = rule.Enabled;
            item._shield = rule.Shield;
            item._kill = rule.Kill;
            item.RefreshDisplay();
            return item;
        }

        public AppRule Rule
        {
            get { return _rule; }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (_rule != null) _rule.Enabled = value;
                Raise("Enabled");
            }
        }

        public string Network
        {
            get { return _network; }
            set
            {
                if (_network == value) return;
                _network = value;
                if (_rule != null) _rule.Network = value;
                Raise("Network");
                Raise("NetworkLabel");
                Raise("NetworkHint");
            }
        }

        public string NetworkLabel
        {
            get { return NetworkModes.Label(_network); }
        }

        public string NetworkHint
        {
            get { return NetworkModes.Hint(_network); }
        }

        public bool Shield
        {
            get { return _shield; }
            set
            {
                if (_shield == value) return;
                _shield = value;
                if (_rule != null) _rule.Shield = value;
                Raise("Shield");
            }
        }

        public bool Kill
        {
            get { return _kill; }
            set
            {
                if (_kill == value) return;
                _kill = value;
                if (_rule != null) _rule.Kill = value;
                Raise("Kill");
            }
        }

        public AppRule ToRule()
        {
            AppRule rule = _rule ?? new AppRule();
            rule.Id = Id;
            rule.Name = Name;
            rule.Path = Path;
            rule.Root = Root;
            rule.Scope = Scope;
            rule.Network = _network;
            rule.Enabled = _enabled;
            rule.Shield = _shield;
            rule.Kill = _kill;
            rule.Normalize();
            return rule;
        }

        public void RefreshDisplay()
        {
            Exists = !string.IsNullOrEmpty(Path) && File.Exists(Path);
            ShortPath = Shorten(Path);
            if (Exists)
            {
                StatusText = "已安装";
                StatusBrush = Frozen("#34D399");
            }
            else
            {
                StatusText = "路径不存在";
                StatusBrush = Frozen("#FBBF24");
            }
            Icon = LoadIcon(Path);
            Raise("ShortPath");
            Raise("StatusText");
            Raise("StatusBrush");
            Raise("Icon");
            Raise("Exists");
        }

        private static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(local) && path.StartsWith(local, StringComparison.OrdinalIgnoreCase))
                return "%LOCALAPPDATA%" + path.Substring(local.Length);
            if (!string.IsNullOrEmpty(roaming) && path.StartsWith(roaming, StringComparison.OrdinalIgnoreCase))
                return "%APPDATA%" + path.Substring(roaming.Length);
            if (!string.IsNullOrEmpty(programFiles) && path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
                return "%PROGRAMFILES%" + path.Substring(programFiles.Length);
            if (!string.IsNullOrEmpty(programFilesX86) && path.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
                return "%PROGRAMFILES(X86)%" + path.Substring(programFilesX86.Length);
            return path;
        }

        private static System.Windows.Media.Brush Frozen(string color)
        {
            SolidColorBrush brush = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private static ImageSource LoadIcon(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(28, 28));
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class HardeningItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Recommended { get; set; }
        public bool NeedsSignOut { get; set; }
        public string IconGlyph { get; set; }
        public string WarningText { get; set; }

        public HardeningItem()
        {
            Title = "";
            Description = "";
            IconGlyph = "\uE72E";
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                Raise("IsEnabled");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ChecklistItem
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string State { get; set; }
        public string Glyph { get; set; }
        public System.Windows.Media.Brush StateBrush { get; set; }

        public ChecklistItem()
        {
            Title = "";
            Detail = "";
            State = "ok";
            Glyph = "\uE73E";
            StateBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
        }
    }
}


