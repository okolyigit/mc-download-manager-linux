using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace MCDM;

/// Linux counterpart of upstream's App.xaml.cs: tray icon, single instance, clipboard, notifications, start at login.
public partial class App : Application
{
    public static AppSettings Settings = null!;
    public static DownloadStore Store = null!;
    public static BridgeServer Bridge = null!;
    public static WindowIcon Icon = null!;

    IClassicDesktopStyleApplicationLifetime _desktop = null!;
    MainWindow? _main;
    TrayIcon? _tray;

    public static App Instance => (App)Current!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://mc-download-manager/app.ico")));

            Settings = AppSettings.Load();
            Store = new DownloadStore(Settings);
            Bridge = new BridgeServer(Store);
            Bridge.Start(Settings.ServerPort);

            Store.Added = item => { if (Settings.ShowDetailOnStart) DetailWindow.ShowFor(item); };
            Store.Completed = OnCompleted;
            Store.AskInstallTools = () =>
            {
                // Upstream installs Windows builds; on Linux the tools come from the package manager.
                _ = Dialogs.Ask(S.T("Video tools are missing", "Video araçları eksik"), VideoToolsHint, new[] { S.Ok });
                return false;
            };

            SingleInstance.Listen(() => Dispatcher.UIThread.Post(ShowMain));
            CopyExtension();
            SetupTray();
            StartClipboardWatcher();
            desktop.ShutdownRequested += (_, _) => Store.Shutdown();

            // Launched at login → stay in the tray. Launched by the user → show the window.
            if (!(desktop.Args ?? Array.Empty<string>()).Contains("--hidden")) ShowMain();
            if (!Settings.OnboardingDone) Dispatcher.UIThread.Post(() => _ = Onboarding());
            if (Environment.GetEnvironmentVariable("MCDM_SNAPSHOT_DIR") is { Length: > 0 } snapDir) Snapshots(snapDir);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static string VideoToolsHint => S.T(
        "Video downloads need yt-dlp and ffmpeg, and deno for YouTube. Install them with your package manager, for example:\n\nArch: sudo pacman -S yt-dlp ffmpeg deno\nDebian/Ubuntu: sudo apt install yt-dlp ffmpeg\nFedora: sudo dnf install yt-dlp ffmpeg",
        "Video indirmek için yt-dlp ve ffmpeg, YouTube için ayrıca deno gerekir. Bunları paket yöneticinizle kurun, örneğin:\n\nArch: sudo pacman -S yt-dlp ffmpeg deno\nDebian/Ubuntu: sudo apt install yt-dlp ffmpeg\nFedora: sudo dnf install yt-dlp ffmpeg");

    async Task Onboarding()
    {
        Settings.OnboardingDone = true;
        Directory.CreateDirectory(Settings.DownloadDirectory);
        var r = await Dialogs.Ask(S.T("Start at login?", "Oturum açılınca başlatılsın mı?"),
            S.T("MC Download Manager can start in the system tray when you log in, so browser downloads are captured without opening the app. You can change this in Settings.",
                "MC Download Manager oturum açtığınızda sistem tepsisinde başlayabilir; böylece uygulamayı açmadan tarayıcı indirmeleri yakalanır. Bunu Ayarlar'dan değiştirebilirsiniz."),
            new[] { S.T("Start at Login", "Oturumla Başlat"), S.T("No", "Hayır") });
        Autostart.Enabled = r.Button == 0;
        Settings.Save();
    }

    /// Development aid: renders the app's own windows to PNG files (never the screen), e.g. to review the UI.
    void Snapshots(string dir)
    {
        static void Save(Window w, string path)
        {
            var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
            rtb.Render(w);
            rtb.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        DispatcherTimer.RunOnce(() =>
        {
            Directory.CreateDirectory(dir);
            Save(Main, Path.Combine(dir, "main.png"));
            foreach (var w in DetailWindow.OpenWindows) Save(w, Path.Combine(dir, $"detail-{w.Title?.GetHashCode():x}.png"));
            SettingsWindow.ShowSingle();
            DispatcherTimer.RunOnce(() => { if (SettingsWindow.Current is { } sw) Save(sw, Path.Combine(dir, "settings.png")); }, TimeSpan.FromSeconds(1));
        }, TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("MCDM_SNAPSHOT_DELAY"), out var d) ? d : 3));
    }

    public void ShowMain()
    {
        _main ??= new MainWindow();
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public MainWindow Main => _main ??= new MainWindow();

    public void Quit()
    {
        _tray?.Dispose();
        _desktop.Shutdown();
    }

    // MARK: - Tray

    void SetupTray()
    {
        var menu = new NativeMenu();
        var status = new NativeMenuItem { IsEnabled = false };
        void Item(string text, Action click)
        {
            var mi = new NativeMenuItem(text);
            mi.Click += (_, _) => click();
            menu.Items.Add(mi);
        }
        menu.Items.Add(status);
        menu.Items.Add(new NativeMenuItemSeparator());
        Item(S.T("Show Window", "Pencereyi Göster"), ShowMain);
        Item(S.AddDownload + "…", () => { ShowMain(); _ = Main.ShowAddDialog(); });
        menu.Items.Add(new NativeMenuItemSeparator());
        Item(S.PauseAll, Store.PauseAll);
        Item(S.ResumeAll, Store.ResumeAll);
        menu.Items.Add(new NativeMenuItemSeparator());
        Item(S.Settings + "…", SettingsWindow.ShowSingle);
        Item(S.T("Quit MC Download Manager", "MC Download Manager'dan Çık"), Quit);

        _tray = new TrayIcon { Icon = Icon, ToolTipText = "MC Download Manager", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });

        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            status.Header = Store.ActiveCount > 0
                ? S.T($"{Store.ActiveCount} downloading · {Fmt.Speed(Store.TotalSpeed)}", $"{Store.ActiveCount} indiriliyor · {Fmt.Speed(Store.TotalSpeed)}")
                : S.T("No active downloads", "Etkin indirme yok");
            var text = "MC Download Manager";
            if (Store.ActiveCount > 0)
                text += $"\n{Store.ActiveCount} · {(Store.OverallProgress is double p ? $"{(int)(p * 100)}% · " : "")}{Fmt.Speed(Store.TotalSpeed)}";
            if (_tray.ToolTipText != text) _tray.ToolTipText = text;
        }).Start();
    }

    void OnCompleted(DownloadItem item)
    {
        if (Settings.NotifyOnComplete)
            Notifier.Show(S.T("Download completed", "İndirme tamamlandı"), item.FileName, S.ShowInFolder, () => Store.ShowInFolder(item));
        DetailWindow.DidComplete(item, Settings.AutoCloseDetail);
    }

    // MARK: - Browser extension

    /// Stable folder for "Load unpacked" (refreshed on every start).
    public static string ExtensionDir => Path.Combine(AppSettings.DataDir, "BrowserExtension");

    static void CopyExtension()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Extension");
        if (!Directory.Exists(src)) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(ExtensionDir, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
        }
        catch { }
    }

    // MARK: - Clipboard (same rules as upstream)

    static readonly string[] VideoHosts = { "youtube.com", "youtu.be", "vimeo.com", "dailymotion.com", "twitter.com", "x.com",
        "instagram.com", "tiktok.com", "twitch.tv", "facebook.com", "reddit.com", "bilibili.com" };

    string? _lastClip;
    readonly List<string> _askedUrls = new();
    bool _prompting;

    public static Task<string?> ReadClipboard() =>
        TopLevel.GetTopLevel(Instance.Main)?.Clipboard?.TryGetTextAsync() ?? Task.FromResult<string?>(null);

    public static void WriteClipboard(string text) => TopLevel.GetTopLevel(Instance.Main)?.Clipboard?.SetTextAsync(text);

    async void StartClipboardWatcher()
    {
        // No change counter on Linux: remember what was there at startup and react to new text only.
        try { _lastClip = await ReadClipboard(); } catch { }
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await PollClipboard()).Start();
    }

    async Task PollClipboard()
    {
        if (!Settings.WatchClipboard || _prompting) return;
        string? text;
        try { text = await ReadClipboard(); }
        catch { return; }
        if (string.IsNullOrEmpty(text) || text == _lastClip) return;
        _lastClip = text;
        // Only react when the clipboard holds nothing but links to files of a captured type.
        var exts = Settings.CaptureExtensions;
        var urls = UrlOnlyContent(text)?.Where(u => Categories.Matches(u, exts) && !_askedUrls.Contains(u.AbsoluteUri)).ToList();
        if (urls is not { Count: > 0 }) return;
        await AskDownload(urls, fromClipboard: true);
    }

    /// Ctrl+V in the main window (explicit action: any http(s) link is accepted).
    public async Task HandlePaste(string text)
    {
        if (UrlOnlyContent(text) is { Count: > 0 } urls) await AskDownload(urls, fromClipboard: false);
    }

    /// The links if every non-empty line is a single http(s) URL; null otherwise.
    static List<Uri>? UrlOnlyContent(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines.Length > 50) return null;
        var urls = new List<Uri>();
        foreach (var l in lines)
        {
            if (l.Contains(' ') || !Uri.TryCreate(l, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || u.Host == "") return null;
            if (!urls.Contains(u)) urls.Add(u);
        }
        return urls;
    }

    static bool LooksLikeVideoPage(Uri u)
    {
        if (Categories.Detect(u.AbsolutePath, DownloadKind.File) != FileCategory.Other) return false;
        var host = u.Host.ToLowerInvariant();
        return VideoHosts.Any(h => host == h || host.EndsWith("." + h));
    }

    async Task AskDownload(List<Uri> urls, bool fromClipboard)
    {
        _askedUrls.AddRange(urls.Select(u => u.AbsoluteUri));
        if (_askedUrls.Count > 50) _askedUrls.RemoveRange(0, _askedUrls.Count - 50);
        _prompting = true;
        bool preferVideo = urls.All(LooksLikeVideoPage);
        string title = urls.Count == 1
            ? (fromClipboard ? S.T("A link was copied. Download it?", "Bir bağlantı kopyalandı. İndirilsin mi?") : S.T("Download the pasted link?", "Yapıştırılan bağlantı indirilsin mi?"))
            : (fromClipboard ? S.T($"{urls.Count} links were copied. Download them?", $"{urls.Count} bağlantı kopyalandı. İndirilsin mi?") : S.T($"Download the {urls.Count} pasted links?", $"Yapıştırılan {urls.Count} bağlantı indirilsin mi?"));
        string body = string.Join("\n", urls.Take(6).Select(u => u.AbsoluteUri)) + (urls.Count > 6 ? "\n…" : "");
        var buttons = preferVideo ? new[] { S.DownloadVideo, S.T("Download as File", "Dosya Olarak İndir") } : new[] { S.Download, S.DownloadVideo };
        var r = await Dialogs.Ask(title, body, buttons.Append(S.T("Ignore", "Yoksay")).ToArray(),
            fromClipboard ? S.T("Stop watching the clipboard", "Panoyu izlemeyi bırak") : null);
        _prompting = false;
        try { _lastClip = await ReadClipboard(); } catch { }
        if (r.Checked) { Settings.WatchClipboard = false; Settings.Save(); }
        if (r.Button is not (0 or 1)) return;
        var kind = (r.Button == 0) == preferVideo ? DownloadKind.Video : DownloadKind.File;
        foreach (var u in urls) Store.Add(new NewDownloadRequest { Url = u, Kind = kind });
    }
}
