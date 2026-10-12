using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MCDM;

/// Mirrors upstream's App.xaml.cs startup; tray, clipboard and dialogs come in the UI step.
public partial class App : Application
{
    public static AppSettings Settings = null!;
    public static DownloadStore Store = null!;
    public static BridgeServer Bridge = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Settings = AppSettings.Load();
            Store = new DownloadStore(Settings);
            Bridge = new BridgeServer(Store);
            Bridge.Start(Settings.ServerPort);
            Store.AskInstallTools = () => false;   // TODO: dialog; on Linux yt-dlp/ffmpeg usually come from the package manager

            CopyExtension();
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) => Store.Shutdown();
        }
        base.OnFrameworkInitializationCompleted();
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
}
