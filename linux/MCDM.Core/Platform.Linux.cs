using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MCDM;

/// Linux counterpart of upstream's MCDownloadManager/Platform.Windows.cs (same members).
public static class Platform
{
    public const string ExeSuffix = "";

    /// Opens the file manager with the file selected (org.freedesktop.FileManager1, supported by Dolphin, Nautilus, Nemo…),
    /// or falls back to opening the folder.
    public static void ShowInFolder(string path)
    {
        if (File.Exists(path))
        {
            // dbus-send splits array values on ',', so escape it in the URI.
            var uri = new Uri(path).AbsoluteUri.Replace(",", "%2C");
            if (Run("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                    "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", "array:string:" + uri, "string:"))
                return;
            path = Path.GetDirectoryName(path)!;
        }
        Run("xdg-open", path);
    }

    /// Freedesktop trash through gio (GNOME/GTK) or kioclient (KDE). Throws when neither works; the caller ignores it.
    public static void MoveToTrash(string file)
    {
        if (Run("gio", "trash", file) || Run("kioclient", "move", file, "trash:/") || Run("kioclient5", "move", file, "trash:/")) return;
        throw new IOException("No trash tool (gio or kioclient) found");
    }

    /// The xattrs Chromium and Firefox set on Linux downloads.
    public static void MarkOfTheWeb(string file, string url, string? referer)
    {
        SetXattr(file, "user.xdg.origin.url", url);
        if (referer != null) SetXattr(file, "user.xdg.referrer.url", referer);
    }

    public static string? ExtensionForMime(string mime) =>
        MimeTypes.Value.TryGetValue(mime.Trim().ToLowerInvariant(), out var ext) ? ext : null;

    // MARK: - Helpers

    /// Runs a tool and waits briefly; true when it exited with 0.
    static bool Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(5000)) return true;   // still running (e.g. xdg-open kept the app attached): treat as launched
            return p.ExitCode == 0;
        }
        catch { return false; }   // not installed
    }

    [DllImport("libc", SetLastError = true)]
    static extern int setxattr(string path, string name, byte[] value, nuint size, int flags);

    static void SetXattr(string file, string name, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        setxattr(file, name, bytes, (nuint)bytes.Length, 0);   // fails silently on filesystems without user xattrs
    }

    /// First extension per type from /etc/mime.types (the mailcap / media-types package).
    static readonly Lazy<Dictionary<string, string>> MimeTypes = new(() =>
    {
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadLines("/etc/mime.types"))
            {
                if (line.StartsWith('#')) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) map.TryAdd(parts[0].ToLowerInvariant(), "." + parts[1]);
            }
        }
        catch { }
        return map;
    });
}
