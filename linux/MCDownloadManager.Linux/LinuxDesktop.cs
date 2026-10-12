using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MCDM;

/// One running instance per user: the first one listens on a Unix socket, later launches send "show" and exit.
static class SingleInstance
{
    static string SocketPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(), "mc-download-manager.sock");

    public static bool SignalRunning()
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            s.Send("show\n"u8);
            return true;
        }
        catch { return false; }   // nothing listening (or a stale socket file)
    }

    public static void Listen(Action onShow)
    {
        try
        {
            File.Delete(SocketPath);
            var l = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            l.Bind(new UnixDomainSocketEndPoint(SocketPath));
            l.Listen(4);
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    using var c = await l.AcceptAsync();
                    onShow();
                }
            });
        }
        catch { }
    }
}

/// "Start at login" through an XDG autostart entry (KDE, GNOME, XFCE…).
static class Autostart
{
    static string EntryPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "mc-download-manager.desktop");

    public static bool Enabled
    {
        get => File.Exists(EntryPath);
        set
        {
            try
            {
                if (!value) { File.Delete(EntryPath); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
                File.WriteAllText(EntryPath,
                    "[Desktop Entry]\nType=Application\nName=MC Download Manager\n" +
                    $"Exec=\"{Environment.ProcessPath}\" --hidden\nIcon=mc-download-manager\nX-GNOME-Autostart-enabled=true\nNoDisplay=true\n");
            }
            catch { }
        }
    }
}

/// Desktop notifications through notify-send (libnotify), with an optional action button.
static class Notifier
{
    public static void Show(string title, string body, string? actionLabel = null, Action? onAction = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false, RedirectStandardOutput = true };
                foreach (var a in new[] { "--app-name=MC Download Manager", "--icon=folder-download" }) psi.ArgumentList.Add(a);
                if (actionLabel != null) psi.ArgumentList.Add("--action=default=" + actionLabel);
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(body);
                using var p = Process.Start(psi)!;
                // With --action, notify-send waits and prints the chosen action.
                var chosen = (await p.StandardOutput.ReadToEndAsync()).Trim();
                if (chosen == "default") Avalonia.Threading.Dispatcher.UIThread.Post(() => onAction?.Invoke());
            }
            catch { }
        });
    }
}
