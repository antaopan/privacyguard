using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrivacyGuard
{
    public partial class ScanWindow : Window
    {
        public ObservableCollection<ScanItem> Items { get; private set; }
        public List<AppRule> Results { get; private set; }

        public ScanWindow()
        {
            InitializeComponent();
            Items = new ObservableCollection<ScanItem>();
            Results = new List<AppRule>();
            DataContext = this;
        }

        public ScanWindow(List<DetectedApp> apps) : this()
        {
            if (apps != null)
            {
                foreach (DetectedApp app in apps)
                {
                    ScanItem item = new ScanItem();
                    item.Id = app.Id;
                    item.Name = app.Name;
                    item.ExePath = app.ExePath;
                    item.Source = app.Source;
                    item.IsSelected = true;
                    item.Icon = LoadIcon(app.ExePath);
                    item.PropertyChanged += Item_PropertyChanged;
                    Items.Add(item);
                }
            }
            UpdateCount();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected") UpdateCount();
        }

        private void UpdateCount()
        {
            int count = Items.Count(x => x.IsSelected);
            if (CountText != null) CountText.Text = "已选择 " + count + " 个";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ScanItem item in Items) item.IsSelected = true;
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (ScanItem item in Items) item.IsSelected = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Results.Clear();
            foreach (ScanItem item in Items.Where(x => x.IsSelected))
            {
                AppRule rule = new AppRule();
                rule.Name = item.Name;
                rule.Path = item.ExePath;
                rule.Scope = string.Equals(item.Id, "codex", StringComparison.OrdinalIgnoreCase) ? Scopes.File : Scopes.Folder;
                rule.Root = AppMatcher.InferRoot(item.ExePath);
                if (string.IsNullOrEmpty(rule.Root))
                {
                    rule.Scope = Scopes.File;
                    rule.Root = "";
                }
                rule.Network = NetworkModes.Inbound;
                rule.Shield = true;
                rule.Kill = false;
                rule.Enabled = true;
                rule.Normalize();
                Results.Add(rule);
            }
            if (Results.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "请至少选择一个应用。", "未选择",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class ScanItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExePath { get; set; }
        public string Source { get; set; }
        public ImageSource Icon { get; set; }

        public ScanItem()
        {
            Id = "";
            Name = "";
            ExePath = "";
            Source = "";
            _isSelected = true;
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("IsSelected"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}


