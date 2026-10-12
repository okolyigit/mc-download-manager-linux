using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MCDM;

/// OS-specific helpers used by the shared engine files. The Linux app has its own Platform class with the same members.
public static class Platform
{
    public const string ExeSuffix = ".exe";

    /// Opens Explorer with the file selected, or opens the folder.
    public static void ShowInFolder(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else Process.Start("explorer.exe", $"\"{path}\"");
    }

    public static void MoveToTrash(string file) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    /// Zone.Identifier stream, like a browser download.
    public static void MarkOfTheWeb(string file, string url, string? referer)
    {
        var body = "[ZoneTransfer]\r\nZoneId=3\r\n" + (referer != null ? $"ReferrerUrl={referer}\r\n" : "") + $"HostUrl={url}\r\n";
        File.WriteAllText(file + ":Zone.Identifier", body);
    }

    public static string? ExtensionForMime(string mime)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"MIME\Database\Content Type\{mime}");
        return key?.GetValue("Extension") as string;
    }
}
