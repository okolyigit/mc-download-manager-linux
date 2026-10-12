using System;
using Avalonia;

namespace MCDM;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // A second launch just brings up the running app's window.
        if (SingleInstance.SignalRunning()) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
