using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Material.Icons;

namespace MCDM;

public sealed class SidebarEntry : INotifyPropertyChanged
{
    public required string Label { get; init; }
    public required MaterialIconKind Icon { get; init; }
    public required Func<DownloadItem, bool> Match { get; init; }
    public Thickness Indent { get; init; }
    public FontWeight Weight { get; init; } = FontWeight.Normal;
    public string CountText { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetCount(int n)
    {
        var t = n > 0 ? n.ToString() : "";
        if (t == CountText) return;
        CountText = t;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
    }
}

/// Port of upstream's MainWindow (WPF) with the macOS sidebar and columns.
public partial class MainWindow : Window
{
    readonly DataGridCollectionView _view;
    readonly List<SidebarEntry> _entries;
    Func<DownloadItem, bool> _sidebarFilter = _ => true;

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.Icon;
        Logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://mc-download-manager/app.ico")));

        // Same order as the macOS sidebar: All, categories, Finished, Unfinished.
        _entries = new List<SidebarEntry> { new() { Label = S.T("All", "Tümü"), Icon = MaterialIconKind.FolderOutline, Match = _ => true, Weight = FontWeight.SemiBold } };
        _entries.AddRange(Enum.GetValues<FileCategory>().Select(c => new SidebarEntry
            { Label = S.CategoryLabel(c), Icon = Converters.CategoryIcon(c), Match = i => i.Category == c, Indent = new Thickness(14, 0, 0, 0) }));
        _entries.Add(new() { Label = S.T("Finished", "Bitenler"), Icon = MaterialIconKind.CheckCircleOutline, Match = i => i.State == DownloadState.Completed, Weight = FontWeight.SemiBold });
        _entries.Add(new() { Label = S.T("Unfinished", "Bitmeyenler"), Icon = MaterialIconKind.ClockOutline, Match = i => i.State != DownloadState.Completed, Weight = FontWeight.SemiBold });
        Sidebar.ItemsSource = _entries;
        Sidebar.SelectedIndex = 0;

        _view = new DataGridCollectionView(App.Store.Items) { Filter = o => o is DownloadItem i && _sidebarFilter(i) && MatchesSearch(i) };
        Grid.ItemsSource = _view;
        App.Store.Items.CollectionChanged += (_, _) => UpdateStatus();

        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateStatus()).Start();
        UpdateStatus();
    }

    /// Closing the window keeps the app running in the tray.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    bool MatchesSearch(DownloadItem i)
    {
        var q = SearchBox.Text?.Trim() ?? "";
        return q.Length == 0 || i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Url.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void UpdateStatus()
    {
        if (!IsVisible) return;
        var s = App.Store;
        foreach (var e in _entries) e.SetCount(s.Items.Count(e.Match));
        // Items move between views when their state or name changes (Unfinished → Finished, category after the probe).
        if (_view.Count != s.Items.Count(i => _view.Filter!(i))) _view.Refresh();
        EmptyState.IsVisible = _view.Count == 0;
        bool searching = (SearchBox.Text ?? "").Trim().Length > 0;
        EmptyTitle.Text = searching ? S.T("No matching downloads", "Eşleşen indirme yok") : S.EmptyTitle;
        EmptyHint.Text = searching ? "" : S.EmptyHint;
        ActiveText.Text = s.ActiveCount.ToString();
        SpeedText.Text = Fmt.Speed(s.TotalSpeed);
        BridgeText.Text = App.Bridge.Running ? S.T("Browser bridge is running", "Tarayıcı köprüsü çalışıyor") : App.Bridge.StatusText;
        BridgeDot.Fill = App.Bridge.Running ? Brushes.LimeGreen : Brushes.IndianRed;
    }

    List<DownloadItem> Selected => Grid.SelectedItems.Cast<DownloadItem>().ToList();

    void Sidebar_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (Sidebar.SelectedItem is not SidebarEntry entry || _view == null) return;
        _sidebarFilter = entry.Match;
        _view.Refresh();
        UpdateStatus();
    }

    void Search_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        UpdateStatus();
    }

    async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;
        if (e.Key == Key.N) { e.Handled = true; await ShowAddDialog(); }
        else if (e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox)
        {
            e.Handled = true;
            if (await App.ReadClipboard() is { Length: > 0 } text) await App.Instance.HandlePaste(text);
        }
    }

    public async Task ShowAddDialog()
    {
        var w = new Window
        {
            Title = S.AddDownload, Icon = App.Icon, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = S.PasteLinks, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { AcceptsReturn = true, Height = 110, TextWrapping = TextWrapping.NoWrap };
        try { if (await App.ReadClipboard() is string clip && clip.TrimStart().StartsWith("http")) box.Text = clip.Trim(); } catch { }
        panel.Children.Add(box);
        var opts = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 14), Spacing = 6 };
        opts.Children.Add(new TextBlock { Text = S.Connections, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var seg = new ComboBox { ItemsSource = Enumerable.Range(1, 16).ToList(), SelectedItem = App.Settings.SegmentCount, Width = 80 };
        opts.Children.Add(seg);
        panel.Children.Add(opts);
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        DownloadKind? kind = null;
        foreach (var (label, k) in new[] { (S.Download, DownloadKind.File), (S.DownloadVideo, DownloadKind.Video) })
        {
            var b = new Button { Content = label, IsDefault = k == DownloadKind.File };
            if (k == DownloadKind.File) b.Classes.Add("accent");
            b.Click += (_, _) => { kind = k; w.Close(); };
            row.Children.Add(b);
        }
        var cancel = new Button { Content = S.Cancel, IsCancel = true };
        cancel.Click += (_, _) => w.Close();
        row.Children.Add(cancel);
        panel.Children.Add(row);
        w.Content = panel;
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        if (IsVisible) await w.ShowDialog(this);
        else { var done = new TaskCompletionSource(); w.Closed += (_, _) => done.TrySetResult(); w.Show(); await done.Task; }
        if (kind is DownloadKind dk) App.Store.AddUrls(box.Text ?? "", dk, (int)seg.SelectedItem!);
    }

    // Context menu: show only what applies to the selection (as on macOS).
    void RowMenu_Opening(object? sender, CancelEventArgs e)
    {
        var items = Selected;
        bool any = items.Count > 0;
        bool completed = items.Any(i => i.State == DownloadState.Completed);
        MenuOpen.IsVisible = MenuFolder.IsVisible = completed;
        MenuDetails.IsVisible = MenuCopy.IsVisible = MenuRemove.IsVisible = any;
        MenuPause.IsVisible = MenuCancel.IsVisible = items.Any(i => i.IsActive || i.State == DownloadState.Queued);
        MenuResume.IsVisible = items.Any(i => i.State is DownloadState.Paused or DownloadState.Failed or DownloadState.Cancelled);
        MenuDelete.IsVisible = completed;
    }

    async void Add_Click(object? s, RoutedEventArgs e) => await ShowAddDialog();
    void Resume_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) App.Store.Resume(i); }
    void Pause_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) App.Store.Pause(i); }
    void Cancel_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) App.Store.Cancel(i); }
    void Remove_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) App.Store.Remove(i); }
    async void RemoveDelete_Click(object? s, RoutedEventArgs e)
    {
        var items = Selected.Where(i => i.State == DownloadState.Completed).ToList();
        if (items.Count == 0) return;
        var r = await Dialogs.Ask(S.T($"Move {items.Count} file(s) to the Trash and remove them from the list?", $"{items.Count} dosya Çöp Kutusu'na taşınıp listeden kaldırılsın mı?"),
            string.Join("\n", items.Take(6).Select(i => i.FileName)), new[] { S.RemoveAndDelete, S.Cancel }, owner: this);
        if (r.Button == 0) foreach (var i in items) App.Store.Remove(i, deleteFile: true);
    }
    void Open_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) App.Store.Open(i); }
    void Folder_Click(object? s, RoutedEventArgs e) { if (Selected.FirstOrDefault() is { } i) App.Store.ShowInFolder(i); }
    void Details_Click(object? s, RoutedEventArgs e) { foreach (var i in Selected) DetailWindow.ShowFor(i); }
    void CopyLink_Click(object? s, RoutedEventArgs e) => App.WriteClipboard(string.Join("\n", Selected.Select(i => i.Url)));
    void PauseAll_Click(object? s, RoutedEventArgs e) => App.Store.PauseAll();
    void ResumeAll_Click(object? s, RoutedEventArgs e) => App.Store.ResumeAll();
    void ClearFinished_Click(object? s, RoutedEventArgs e) => App.Store.ClearFinished();
    void Settings_Click(object? s, RoutedEventArgs e) => SettingsWindow.ShowSingle();

    void Grid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Grid.SelectedItem is not DownloadItem i) return;
        if (i.State == DownloadState.Completed) App.Store.Open(i); else DetailWindow.ShowFor(i);
    }

    void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { Grid_DoubleTapped(sender, null!); e.Handled = true; }
    }
}
