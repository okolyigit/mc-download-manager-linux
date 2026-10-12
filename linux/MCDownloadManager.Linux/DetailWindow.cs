using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace MCDM;

/// Per-download window (macOS DownloadDetailView): info grid, progress, connection table or yt-dlp log; turns into a
/// "completed" view when the download finishes.
public sealed class DetailWindow : Window
{
    static readonly Dictionary<Guid, DetailWindow> Open = new();
    public static IEnumerable<DetailWindow> OpenWindows => Open.Values;

    public static void ShowFor(DownloadItem item)
    {
        if (!Open.TryGetValue(item.Id, out var w))
        {
            w = new DetailWindow(item);
            Open[item.Id] = w;
        }
        w.Show();
        w.Activate();
    }

    public static void DidComplete(DownloadItem item, bool autoClose)
    {
        if (Open.TryGetValue(item.Id, out var w) && (autoClose || w._autoClose.IsChecked == true)) w.Close();
    }

    readonly DownloadItem _item;
    readonly DispatcherTimer _timer;
    bool? _showingCompleted;
    bool _showDetails = true;

    // Active view
    readonly SelectableTextBlock _url = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _status = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _size = new(), _received = new(), _speed = new(), _eta = new(), _resume = new();
    readonly TextBlock _saveTo = new() { TextTrimming = TextTrimming.PathSegmentEllipsis };
    readonly ProgressBar _progress = new() { Maximum = 1, Height = 6 };
    readonly CheckBox _autoClose = new() { Content = S.T("Close when completed", "Tamamlanınca kapat") };
    readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly Button _detailsToggle = new();
    readonly StackPanel _details = new() { Spacing = 6 };
    readonly SegmentBar _bar = new() { Height = 12 };
    readonly Grid _connections = new() { ColumnDefinitions = new ColumnDefinitions("40,110,110,*") };
    readonly TextBox _log = new()
    {
        IsReadOnly = true, Height = 180, FontFamily = new FontFamily("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
    };
    DownloadState? _actionsFor;

    DetailWindow(DownloadItem item)
    {
        _item = item;
        Icon = App.Icon;
        Width = 580;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _autoClose.IsChecked = App.Settings.AutoCloseDetail;
        _autoClose.IsCheckedChanged += (_, _) => { App.Settings.AutoCloseDetail = _autoClose.IsChecked == true; App.Settings.Save(); };
        _detailsToggle.Click += (_, _) => { _showDetails = !_showDetails; _details.IsVisible = _showDetails; UpdateToggle(); };
        UpdateToggle();

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(0.5), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); Open.Remove(item.Id); };
        Refresh();
    }

    void UpdateToggle() => _detailsToggle.Content = _showDetails ? S.T("<< Hide Details", "<< Ayrıntıları Gizle") : S.T("Show Details >>", "Ayrıntıları Göster >>");

    static TextBlock Label(string text) => new() { Text = text, Classes = { "secondary" } };

    static Grid InfoGrid(params (string Label, Control Value)[] rows)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6, ColumnSpacing = 16 };
        for (int r = 0; r < rows.Length; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Label(rows[r].Label);
            Grid.SetRow(l, r);
            Grid.SetRow(rows[r].Value, r);
            Grid.SetColumn(rows[r].Value, 1);
            g.Children.Add(l);
            g.Children.Add(rows[r].Value);
        }
        return g;
    }

    // MARK: - Active

    Control BuildActive()
    {
        var p = new StackPanel { Margin = new Thickness(16), Spacing = 14 };
        p.Children.Add(InfoGrid(
            ("URL", _url),
            (S.T("Status", "Durum"), _status),
            (S.T("File size", "Dosya boyutu"), _size),
            (S.T("Downloaded", "İndirilen"), _received),
            (S.T("Transfer rate", "Aktarım hızı"), _speed),
            (S.T("Time left", "Kalan süre"), _eta),
            (S.T("Resume capability", "Devam ettirilebilir"), _resume),
            (S.T("Save to", "Kayıt yeri"), _saveTo)));
        p.Children.Add(_progress);

        var row = new DockPanel();
        DockPanel.SetDock(_actions, Dock.Right);
        row.Children.Add(_actions);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        left.Children.Add(_detailsToggle);
        left.Children.Add(_autoClose);
        row.Children.Add(left);
        p.Children.Add(row);

        _details.Children.Clear();
        _details.Children.Add(new Separator());
        if (_item.Kind == DownloadKind.Video)
        {
            _details.Children.Add(Label(S.T("yt-dlp output", "yt-dlp çıktısı")));
            _details.Children.Add(_log);
        }
        else
        {
            var caption = Label(S.T("Start positions and download progress by connections", "Bağlantılara göre başlangıç konumları ve indirme durumu"));
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.FontSize = 12;
            _details.Children.Add(caption);
            _details.Children.Add(_bar);
            _details.Children.Add(new ScrollViewer { Content = _connections, MaxHeight = 220 });
        }
        _details.IsVisible = _showDetails;
        p.Children.Add(_details);
        _actionsFor = null;
        return p;
    }

    void UpdateActions(DownloadItem i)
    {
        if (_actionsFor == i.State) return;
        _actionsFor = i.State;
        _actions.Children.Clear();
        void Add(string text, Action click, bool accent = false)
        {
            var b = new Button { Content = text, IsDefault = accent };
            if (accent) b.Classes.Add("accent");
            b.Click += (_, _) => click();
            _actions.Children.Add(b);
        }
        if (i.State is DownloadState.Downloading or DownloadState.Probing or DownloadState.Queued)
        {
            Add(S.Pause, () => App.Store.Pause(i));
            Add(S.Cancel, () => App.Store.Cancel(i));
        }
        else
        {
            Add(S.Resume, () => App.Store.Resume(i), accent: true);
            Add(S.T("Remove from List", "Listeden Kaldır"), () => { App.Store.Remove(i); Close(); });
        }
    }

    string StatusText(DownloadItem i) => i.State switch
    {
        DownloadState.Downloading when i.Kind == DownloadKind.Video =>
            i.Stage.Length > 0 ? i.Stage : S.T("Downloading with yt-dlp…", "yt-dlp ile indiriliyor…"),
        DownloadState.Downloading => S.T("Receiving data…", "Veri alınıyor…"),
        DownloadState.Failed => i.ErrorMessage ?? S.StateLabel(i.State),
        _ => S.StateLabel(i.State),
    };

    void RefreshActive(DownloadItem i)
    {
        _url.Text = i.Url;
        _status.Text = StatusText(i);
        if (i.State == DownloadState.Failed) _status.Foreground = Brushes.IndianRed; else _status.ClearValue(TextBlock.ForegroundProperty);
        ToolTip.SetTip(_status, i.ErrorMessage);
        _size.Text = i.TotalBytes > 0 ? Fmt.Size(i.TotalBytes) : S.T("Unknown", "Bilinmiyor");
        _received.Text = i.TotalBytes > 0 ? $"{Fmt.Size(i.ReceivedBytes)}  ({(int)(i.Progress * 100)}%)" : Fmt.Size(i.ReceivedBytes);
        _speed.Text = i.State == DownloadState.Downloading && i.Speed > 0 ? Fmt.Speed(i.Speed) : "—";
        _eta.Text = i.Eta is double e ? Fmt.Duration(e) : "—";
        _resume.Text = i.Kind == DownloadKind.Video ? "yt-dlp"
            : i.SupportsRanges ? S.T("Yes", "Evet")
            : i.State is DownloadState.Queued or DownloadState.Probing ? "?" : S.T("No", "Hayır");
        _saveTo.Text = i.FinalPath ?? Path.Combine(App.Store.DestinationDirectory(i), i.FileName);
        _progress.Value = i.TotalBytes > 0 ? i.Progress : 0;
        _progress.IsIndeterminate = i.State == DownloadState.Probing || (i.State == DownloadState.Downloading && i.TotalBytes <= 0);
        UpdateActions(i);

        if (i.Kind == DownloadKind.Video)
        {
            var text = i.Log.Length == 0 ? S.T("Waiting…", "Bekleniyor…") : i.Log;
            if (_log.Text != text) { _log.Text = text; _log.CaretIndex = text.Length; }
        }
        else
        {
            _bar.Segments = i.Segments;
            _bar.Total = i.TotalBytes;
            _bar.Progress = i.Progress;
            _bar.State = i.State;
            RefreshConnections(i);
        }
    }

    readonly List<TextBlock[]> _rows = new();

    void RefreshConnections(DownloadItem i)
    {
        var data = new List<(long Received, long Start, string Info)>();
        if (i.Segments.Length == 0)
        {
            if (i.State != DownloadState.Queued)
                data.Add((i.ReceivedBytes, 0, i.State switch
                {
                    DownloadState.Probing => S.T("Sending GET…", "GET gönderiliyor…"),
                    DownloadState.Downloading => S.T("Receiving data… (single stream)", "Veri alınıyor… (tek bağlantı)"),
                    _ => S.StateLabel(i.State),
                }));
        }
        else
            foreach (var s in i.Segments)
                data.Add((s.Received, s.Start,
                    s.IsDone ? S.T("Completed", "Tamamlandı")
                    : i.State == DownloadState.Downloading ? (s.Received == 0 ? S.T("Sending GET…", "GET gönderiliyor…") : S.T("Receiving data…", "Veri alınıyor…"))
                    : S.StateLabel(i.State)));

        if (_rows.Count == 0)
        {
            _connections.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] headers = { S.T("N.", "No"), S.T("Downloaded", "İndirilen"), S.T("Start", "Başlangıç"), S.T("Info", "Bilgi") };
            for (int c = 0; c < 4; c++)
            {
                var h = new TextBlock { Text = headers[c], FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
                Grid.SetColumn(h, c);
                _connections.Children.Add(h);
            }
        }
        while (_rows.Count < data.Count)
        {
            int r = _rows.Count + 1;
            _connections.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = new TextBlock[4];
            for (int c = 0; c < 4; c++)
            {
                cells[c] = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1) };
                Grid.SetRow(cells[c], r);
                Grid.SetColumn(cells[c], c);
                _connections.Children.Add(cells[c]);
            }
            _rows.Add(cells);
        }
        for (int r = 0; r < _rows.Count; r++)
        {
            bool has = r < data.Count;
            foreach (var cell in _rows[r]) cell.IsVisible = has;
            if (!has) continue;
            _rows[r][0].Text = (r + 1).ToString();
            _rows[r][1].Text = Fmt.Size(data[r].Received);
            _rows[r][2].Text = Fmt.Size(data[r].Start);
            _rows[r][3].Text = data[r].Info;
        }
    }

    // MARK: - Completed

    Control BuildCompleted(DownloadItem i)
    {
        var p = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(new MaterialIcon { Kind = MaterialIconKind.CheckCircle, Width = 44, Height = 44, Foreground = Brushes.SeaGreen });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        titles.Children.Add(new TextBlock { Text = S.T("Download completed", "İndirme tamamlandı"), FontSize = 20, FontWeight = FontWeight.Bold });
        titles.Children.Add(new TextBlock { Text = i.FileName, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 470 });
        head.Children.Add(titles);
        p.Children.Add(head);

        var rows = new List<(string, Control)>
        {
            (S.T("Size", "Boyut"), new TextBlock { Text = Fmt.Size(i.TotalBytes) }),
            (S.T("Location", "Konum"), new TextBlock
                { Text = i.FinalPath != null ? Path.GetDirectoryName(i.FinalPath) : App.Store.DestinationDirectory(i), TextTrimming = TextTrimming.PathSegmentEllipsis }),
            ("URL", new SelectableTextBlock { Text = i.Url, TextTrimming = TextTrimming.CharacterEllipsis }),
        };
        if (i.CompletedAt is DateTime d) rows.Add((S.T("Finished", "Bitiş"), new TextBlock { Text = d.ToString("g") }));
        p.Children.Add(InfoGrid(rows.ToArray()));

        var buttons = new DockPanel();
        var close = new Button { Content = S.Close, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var folder = new Button { Content = S.ShowInFolder };
        folder.Click += (_, _) => App.Store.ShowInFolder(i);
        var open = new Button { Content = S.Open, IsDefault = true, Classes = { "accent" } };
        open.Click += (_, _) => { App.Store.Open(i); Close(); };
        right.Children.Add(folder);
        right.Children.Add(open);
        buttons.Children.Add(right);
        p.Children.Add(buttons);
        return p;
    }

    void Refresh()
    {
        var i = _item;
        if (!App.Store.Items.Contains(i)) { Close(); return; }
        bool completed = i.State == DownloadState.Completed;
        if (_showingCompleted != completed)
        {
            _showingCompleted = completed;
            Content = completed ? BuildCompleted(i) : BuildActive();
        }
        Title = i.TotalBytes > 0 && !completed ? $"{(int)(i.Progress * 100)}% · {i.FileName}" : i.FileName;
        if (!completed) RefreshActive(i);
    }
}
