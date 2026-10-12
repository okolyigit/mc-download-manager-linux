using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MCDM;

/// Temporary window to exercise the engine and bridge end to end; replaced by the real UI in the next step.
public sealed class MainWindow : Window
{
    readonly TextBlock _status = new() { Margin = new(0, 0, 0, 8) };

    public MainWindow()
    {
        Title = "MC Download Manager";
        Width = 760;
        Height = 480;

        var url = new TextBox { PlaceholderText = "https://…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var add = new Button { Content = S.Download, Margin = new(8, 0, 0, 0) };
        add.Click += (_, _) => { App.Store.AddUrls(url.Text ?? "", DownloadKind.File); url.Text = ""; };
        var top = new DockPanel { Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(add, Dock.Right);
        top.Children.Add(add);
        top.Children.Add(url);

        var list = new ListBox
        {
            ItemsSource = App.Store.Items,
            ItemTemplate = new FuncDataTemplate<DownloadItem>((item, _) =>
            {
                var t = new TextBlock();
                // Upstream raises PropertyChanged("") on every change; rebuild the line from the item.
                void Update() => t.Text = $"{item.FileName}   {item.SizeText}   {item.StatusText}   {item.SpeedText}";
                Update();
                item.PropertyChanged += (_, _) => Update();
                return t;
            }),
        };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is DownloadItem i) App.Store.ShowInFolder(i); };

        var root = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(_status);
        root.Children.Add(top);
        root.Children.Add(list);
        Content = root;

        UpdateStatus();
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateStatus()).Start();
    }

    void UpdateStatus() =>
        _status.Text = $"{App.Bridge.StatusText} · {App.Store.ActiveCount} {S.T("active", "etkin")} · {Fmt.Speed(App.Store.TotalSpeed)}"
            + $" · {S.T("Extension folder", "Eklenti klasörü")}: {App.ExtensionDir}";
}
