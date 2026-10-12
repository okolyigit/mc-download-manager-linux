using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;

namespace MCDM;

/// Small prompt with custom button labels and an optional checkbox (upstream's Dialogs.Ask, async).
public static class Dialogs
{
    public static async Task<(int Button, bool Checked)> Ask(string title, string message, string[] buttons, string? checkbox = null, Window? owner = null)
    {
        int result = -1;
        var w = new Window
        {
            Title = "MC Download Manager",
            Icon = App.Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 560,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Topmost = owner == null,
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        CheckBox? cb = checkbox == null ? null : new CheckBox { Content = checkbox, Margin = new Thickness(0, 0, 0, 14) };
        if (cb != null) panel.Children.Add(cb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = buttons[i], IsDefault = i == 0, IsCancel = i == buttons.Length - 1, Padding = new Thickness(14, 6) };
            if (i == 0) b.Classes.Add("accent");
            b.Click += (_, _) => { result = idx; w.Close(); };
            row.Children.Add(b);
        }
        panel.Children.Add(row);
        w.Content = panel;
        if (owner is { IsVisible: true }) await w.ShowDialog(owner);
        else
        {
            var done = new TaskCompletionSource();
            w.Closed += (_, _) => done.TrySetResult();
            w.Show();
            w.Activate();
            await done.Task;
        }
        return (result, cb?.IsChecked == true);
    }
}

/// Bar showing how much of each segment has been received (macOS SegmentBar).
public sealed class SegmentBar : Control
{
    public static readonly StyledProperty<SegmentInfo[]> SegmentsProperty = AvaloniaProperty.Register<SegmentBar, SegmentInfo[]>(nameof(Segments), Array.Empty<SegmentInfo>());
    public static readonly StyledProperty<long> TotalProperty = AvaloniaProperty.Register<SegmentBar, long>(nameof(Total));
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<SegmentBar, double>(nameof(Progress));
    public static readonly StyledProperty<DownloadState> StateProperty = AvaloniaProperty.Register<SegmentBar, DownloadState>(nameof(State));

    public SegmentInfo[] Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public long Total { get => GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public DownloadState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    static SegmentBar() => AffectsRender<SegmentBar>(SegmentsProperty, TotalProperty, ProgressProperty, StateProperty);

    public SegmentBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    IBrush Tint => State switch
    {
        DownloadState.Paused => Brushes.Orange,
        DownloadState.Failed => Brushes.IndianRed,
        _ => this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var c) && c is Color color ? new SolidColorBrush(color) : Brushes.DodgerBlue,
    };

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var radius = size.Height / 2;
        var bg = new Rect(size);
        ctx.DrawRectangle(new SolidColorBrush(Colors.Gray, 0.25), null, bg, radius, radius);
        using var clip = ctx.PushClip(new RoundedRect(bg, radius));
        var tint = Tint;
        var segs = Segments;
        if (segs.Length == 0 || Total <= 0)
        {
            ctx.FillRectangle(tint, new Rect(0, 0, size.Width * Math.Clamp(Progress, 0, 1), size.Height));
            return;
        }
        foreach (var s in segs)
        {
            if (s.Received <= 0) continue;
            double x = (double)s.Start / Total * size.Width, w = (double)s.Received / Total * size.Width;
            ctx.FillRectangle(tint, new Rect(x, 0, Math.Max(w, 1), size.Height));
        }
        var tick = new SolidColorBrush(Colors.White, 0.6);
        for (int i = 1; i < segs.Length; i++)
            ctx.FillRectangle(tick, new Rect((double)segs[i].Start / Total * size.Width, 0, 1, size.Height));
    }
}

public static class Converters
{
    public static MaterialIconKind CategoryIcon(FileCategory c) => c switch
    {
        FileCategory.Image => MaterialIconKind.FileImageOutline,
        FileCategory.Music => MaterialIconKind.FileMusicOutline,
        FileCategory.Video => MaterialIconKind.FileVideoOutline,
        FileCategory.Apps => MaterialIconKind.PackageVariantClosed,
        FileCategory.Document => MaterialIconKind.FileDocumentOutline,
        FileCategory.Compressed => MaterialIconKind.FolderZipOutline,
        _ => MaterialIconKind.FileOutline,
    };

    public static readonly IValueConverter Icon = new FuncValueConverter<FileCategory, MaterialIconKind>(CategoryIcon);

    public static string EtaHeader => S.T("Time Left", "Kalan");

    /// Time left (null unless the size and speed are known).
    public static readonly IValueConverter Eta = new FuncValueConverter<double?, string>(e => e is double v ? Fmt.Duration(v) : "");

    /// "Date Added": time today, date otherwise.
    public static readonly IValueConverter Added = new FuncValueConverter<DateTime, string>(d =>
        d.Date == DateTime.Today ? d.ToString("t", CultureInfo.CurrentCulture) : d.ToString("g", CultureInfo.CurrentCulture));

    /// Styled red through the "error" class (App.axaml).
    public static readonly IValueConverter IsFailed = new FuncValueConverter<DownloadState, bool>(s => s == DownloadState.Failed);
}
