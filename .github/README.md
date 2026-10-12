<p align="center">
  <img src="../MCDownloadManager/app.ico" width="96" alt="MC Download Manager icon">
</p>

<h1 align="center">MC Download Manager for Linux</h1>

<p align="center">
  An open-source download manager for Linux desktops.<br>
  Takes over downloads from Chrome, Chromium, Brave and Edge, speeds them up with up to 16 parallel connections and downloads videos from web pages with yt-dlp.
</p>

<p align="center">
  <a href="README.tr.md">🇹🇷 Türkçe</a> · <a href="#build-and-run">Build</a> · <a href="#browser-extension">Browser extension</a> · <a href="#keeping-up-with-upstream">Upstream sync</a>
</p>

<p align="center">
  <img src="screenshots/main.png" width="820" alt="Main window: sidebar with categories, download list with segment bars, speed and time left">
</p>

---

This is the Linux version of **MC Download Manager**. It comes from a chain of ports:

1. [mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager) by Mesut Çevik, the original macOS app (Swift).
2. [azadkarakus/mc-download-manager-windows](https://github.com/azadkarakus/mc-download-manager-windows), a C# / WPF port for Windows.
3. This repository, which runs the Windows port's C# engine on Linux with an [Avalonia](https://avaloniaui.net/) user interface.

The download engine, the browser bridge and the browser extension are **not rewritten**: they are compiled straight from the Windows port's source files, so fixes made there reach this app with a `git merge`.
Only the user interface and a handful of operating-system calls are Linux-specific.

The interface is in **English** and **Turkish** (it follows `LANG`) and follows the system light/dark theme.

> **Status:** early. Tested on CachyOS (Arch) with KDE Plasma 6 on Wayland, with Chrome.
> Other distributions and desktops should work but have not been tested yet. Ready-made packages (AppImage, AUR) are planned; for now, [build from source](#build-and-run).

## Features

- **Multi-connection downloads**: files are split into HTTP `Range` requests (8 connections by default, 1–16 configurable).
  Falls back to a single stream when the server doesn't support ranges and backs off on `429`/`503` throttling.
- **Pause and resume**: the state of every segment is saved to disk, so downloads continue where they stopped, even after a restart.
- **Browser integration**: the browser's download is cancelled and handed to the app together with cookies, `Referer` and `User-Agent`.
  Which file types are captured is configurable.
- **Video downloads**: "Download video from this page" in the extension uses yt-dlp, with ffmpeg to merge video and audio.
  The default output is MP4 up to 1080p; container and quality are configurable.
- **Clipboard**: copy a link to a file and the app asks whether to download it. `Ctrl+V` pastes links into the main window and `Ctrl+N` adds a new download.
- **Download window**: per-download details with a segment bar and the state of every connection, or the yt-dlp output for videos.

  <img src="screenshots/detail.png" width="440" alt="Download window showing URL, size, speed, time left and eight connections">

- **Linux desktop integration**:
  - system tray icon (StatusNotifierItem), so the app keeps running when the window is closed
  - desktop notifications with a "Show in Folder" button
  - "Show in Folder" selects the file in Dolphin, Nautilus, Nemo and other file managers that implement `org.freedesktop.FileManager1`
  - "Remove and Delete File" moves the file to the Trash
  - optional start at login through an XDG autostart entry
  - a second launch brings the running app's window to the front

## Requirements

| | Needed for | Arch / CachyOS | Debian / Ubuntu | Fedora |
|---|---|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | building | `dotnet-sdk` | [Microsoft's instructions](https://learn.microsoft.com/dotnet/core/install/linux) | `dotnet-sdk-10.0` |
| yt-dlp, ffmpeg | video downloads | `yt-dlp ffmpeg` | `yt-dlp ffmpeg` | `yt-dlp ffmpeg` |
| deno | YouTube videos | `deno` | [deno.com](https://docs.deno.com/runtime/getting_started/installation/) | [deno.com](https://docs.deno.com/runtime/getting_started/installation/) |
| `notify-send` | notifications | `libnotify` | `libnotify-bin` | `libnotify` |
| `gio` or `kioclient` | moving files to the Trash | `glib2` or `kio` | `libglib2.0-bin` | `glib2` |

A desktop with a system tray (KDE Plasma, XFCE, Cinnamon, or GNOME with the *AppIndicator* extension) is recommended,
because closing the window keeps the app running in the tray.

Unlike the Windows version, the app does not download yt-dlp, ffmpeg or deno itself; install them with your package manager.
It finds them on `PATH`, and *Settings ▸ Video* shows which versions it found.

## Build and run

```bash
git clone https://github.com/okolyigit/mc-download-manager-linux.git
cd mc-download-manager-linux/linux

dotnet run --project MCDownloadManager.Linux     # build and start the app
dotnet run --project Tests                       # engine and bridge self-check
```

To build a standalone copy that runs without .NET installed:

```bash
dotnet publish MCDownloadManager.Linux -c Release -r linux-x64 --self-contained -o ~/.local/opt/mc-download-manager
~/.local/opt/mc-download-manager/mc-download-manager
```

To add it to the application menu, create `~/.local/share/applications/mc-download-manager.desktop`:

```ini
[Desktop Entry]
Type=Application
Name=MC Download Manager
Comment=Download manager with browser integration
Exec=/home/YOUR_USER/.local/opt/mc-download-manager/mc-download-manager
Icon=folder-download
Categories=Network;FileTransfer;
```

Replace `YOUR_USER` with your user name. On the first start the app asks whether it should start at login.

## Browser extension

The app copies the browser extension to `~/.local/share/MC Download Manager/BrowserExtension` every time it starts
(*Settings ▸ Browser* shows the folder and has an *Open Folder* button).

**Chrome, Chromium, Brave, Edge**

1. Open `chrome://extensions`.
2. Turn on *Developer mode*.
3. Click *Load unpacked* and select the folder above. It is a hidden folder: press `Ctrl+H` in the file dialog to show it.

Keep the app running (it can stay in the tray) and download a file: the download moves from the browser to the app.
The extension's popup shows whether it can reach the app.

**Firefox** has not been tested on Linux yet. The extension is the same as upstream's; see the [upstream instructions](../README.md#browser-extension).

## Where things are stored

| Path | Contents |
|---|---|
| `~/Downloads/MC Download Manager/` | downloaded files, sorted into category folders (can be changed in *Settings*) |
| `~/.local/share/MC Download Manager/settings.json` | settings |
| `~/.local/share/MC Download Manager/downloads.json` | the download list and the segment state used for resuming |
| `~/.local/share/MC Download Manager/cookies/` | cookies the extension passes for video downloads |
| `~/.local/share/MC Download Manager/BrowserExtension/` | the browser extension, refreshed on every start |
| `~/.config/autostart/mc-download-manager.desktop` | only when *Start at login* is on |

Unfinished downloads are kept next to the destination as hidden `.dm-<id>.part` files.

## Security

- The bridge listens only on `127.0.0.1:48213` and only answers browser-extension origins (`chrome-extension://`, `moz-extension://`)
  with a loopback `Host`. Requests from ordinary web pages, including `Origin: null`, are rejected.
- Cookies and `Authorization` are sent only to the host the download was started for, never to a host reached through a redirect.
- Downloaded files get the `user.xdg.origin.url` and `user.xdg.referrer.url` extended attributes, the same ones Chrome and Firefox set on Linux.
- File names are cleaned with Windows' rules on Linux too, so downloads can be copied to NTFS or exFAT drives without errors.

## How it is built

```
linux/
  MCDM.Core/                     class library: upstream's engine files, compiled in place
    Compat/WpfDispatcher.cs      the bit of WPF's Dispatcher the engine uses, backed by Avalonia
    Platform.Linux.cs            file manager, Trash, xattrs, MIME types
  MCDownloadManager.Linux/       the Avalonia app
    App.axaml(.cs)               tray icon, clipboard, notifications, single instance
    MainWindow.axaml(.cs)        sidebar, list, toolbar, status bar
    DetailWindow.cs              per-download window
    SettingsWindow.cs            settings
    LinuxDesktop.cs              single instance socket, autostart entry, notify-send
  Tests/                         runs upstream's Tests/Program.cs against MCDM.Core

MCDownloadManager/               upstream's Windows app; its engine files are shared
Extension/                       upstream's browser extension (Manifest V3), shared unchanged
```

`MCDM.Core` compiles these upstream files without copying them:
`Models.cs`, `SegmentedDownloader.cs`, `DownloadStore.cs`, `BridgeServer.cs` and `Tools.cs`.
The few Windows calls in them (Explorer, Recycle Bin, registry, `Zone.Identifier`) go through a small `Platform` class:
upstream's Windows app has `MCDownloadManager/Platform.Windows.cs`, and this app has `linux/MCDM.Core/Platform.Linux.cs`.

## Keeping up with upstream

The `main` branch contains upstream's full history.

```bash
git remote add upstream https://github.com/azadkarakus/mc-download-manager-windows.git
git fetch upstream
git merge upstream/main

cd linux
dotnet run --project Tests                       # must print ALL PASSED
dotnet build MCDownloadManager.Linux
```

Changes to upstream's engine files come in with the merge.
Conflicts can only happen in the few lines this repository changed in upstream files (see commit `Move Windows-specific calls behind a Platform class`).

To check that the shared files still build for Windows, compile upstream's app from Linux:

```bash
dotnet build MCDownloadManager -p:EnableWindowsTargeting=true
```

The original macOS app ([mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager)) usually gets new features first, such as browser extension updates.
They reach this app once they reach the Windows port, or can be ported here directly.

## Contributing

Issues and pull requests are welcome. Please run the self-check (`dotnet run --project linux/Tests`) before sending a pull request.

- Linux-specific changes belong in `linux/`.
- Fixes to the download engine, the bridge or the extension are better sent to the [Windows port](https://github.com/azadkarakus/mc-download-manager-windows),
  so all versions get them; this repository picks them up with the next merge.
- UI strings are in English and Turkish, written as `S.T("English", "Türkçe")`.

## Credits and license

- [Mesut Çevik](https://github.com/mstcvk): the original macOS app, the browser extension and the bridge API.
- [azadkarakus](https://github.com/azadkarakus): the Windows port, whose engine this app runs.
- Linux port: Özcan KOLYİĞİT.

Licensed under the MIT License, see [LICENSE](../LICENSE).
yt-dlp (Unlicense), FFmpeg (LGPL/GPL) and deno (MIT) are separate programs installed by the user; they are not part of this repository.
Icons: [Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache 2.0) through [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons) (MIT).
