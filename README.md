# camonlinux

A simple, modern webcam app for Linux — take photos and record videos with your
webcam. Built with **C# / .NET 10**, **Avalonia UI**, **CommunityToolkit.Mvvm**
and **FluentAvalonia**, with a **GStreamer** capture backend.

Inspired by KDE's Kamoso, but written from scratch in C#.

## Features (MVP)

- Live webcam preview (GStreamer `v4l2src` → `appsink`, rendered on a `WriteableBitmap`)
- Take photos (JPEG, saved to `~/Pictures`)
- **Burst mode** — take a photo every 2.5 s
- **Effects gallery** — 25 live GStreamer effects (kaleidoscope, twirl, sepia, mirror, aging TV, …) applied to preview, photos and recordings, each with a **live thumbnail preview** rendered from a camera frame
- Record videos (H.264 + Matroska `.mkv`, saved to `~/Videos`, with on-screen timer)
- Mirror toggle
- Camera selection with friendly names (e.g. "Logitech Webcam C930e" — read from sysfs, deduped to real capture nodes)
- Recent captures gallery with delete (moves to trash)
- Desktop notifications (`notify-send`)
- Settings persisted to `~/.config/camonlinux/settings.json`

## Stack

| Piece | Choice |
|---|---|
| Language / runtime | C# / .NET 10 (`net10.0`) |
| UI framework | Avalonia 12.1.1 |
| UI theme (no manual styling) | FluentAvalonia 3.0.2 (MIT) |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| Capture backend | GStreamer via GirCore 0.8.1 (`GirCore.Gst-1.0` etc.) |
| Photo encoding | SkiaSharp |
| License | MIT |

## Requirements (EndeavourOS / Arch)

```bash
# .NET SDK
sudo pacman -S dotnet-sdk

# GStreamer + plugins (video capture, H.264 encode, Matroska mux, effects)
sudo pacman -S gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-plugins-ugly gstreamer-vaapi

# Desktop notifications
sudo pacman -S libnotify

# Make sure your user can access the webcam (re-login after this!)
sudo usermod -aG video $USER
```

## Build & run

```bash
cd camonlinux
dotnet run --project camonlinux
# or
dotnet build
./camonlinux/bin/Debug/net10.0/camonlinux
```

## Publish a release binary

```bash
# Framework-dependent (smaller; needs the .NET runtime installed)
dotnet publish camonlinux -c Release

# Self-contained (portable single-folder app, no runtime needed)
dotnet publish camonlinux -c Release -r linux-x64 --self-contained true
```

## Install into the app menu

```bash
mkdir -p ~/.local/share/applications ~/.local/share/icons/hicolor/scalable/apps
cp packaging/camonlinux.desktop ~/.local/share/applications/
cp packaging/camonlinux.svg ~/.local/share/icons/hicolor/scalable/apps/camonlinux.svg
# Copy or symlink your built binary into PATH, e.g.:
#   ln -s "$PWD/camonlinux/bin/Release/net10.0/linux-x64/publish/camonlinux" ~/.local/bin/camonlinux
```

## Project structure

```
camonlinux/
├── Models/                # CameraDevice, MediaItem, AppSettings
├── Services/              # Settings, MediaLibrary (watcher), Trash, Notifications
├── Capture/               # ICaptureService + GStreamerCaptureService, CameraFrame
├── Controls/              # VideoSurface (WriteableBitmap renderer)
├── ViewModels/            # MainWindowViewModel (CommunityToolkit MVVM)
├── Views/                 # MainWindow.axaml (FluentAvalonia UI)
└── Assets/                # app icon
```

## How the capture pipeline works

Two separate GStreamer pipelines run at opposite times (a camera device can only
be opened once):

```
# Preview (always when not recording)
v4l2src ! videoconvert ! videoflip (mirror) ! video/x-raw,format=BGRx ! appsink

# Recording (started on demand; preview pauses while recording)
v4l2src ! videoconvert ! videoflip ! x264enc ! matroskamux ! filesink
```

- Frames are pulled from the `appsink` on a background thread
  (`TryPullSample`) and rendered on the preview `WriteableBitmap`.
- Recording finalizes the Matroska container by sending EOS, then the live
  preview automatically resumes.
- **Known limitation:** the preview pauses while recording (a live preview
  during recording via a `tee` is blocked by a GStreamer quirk where any
  buffer-dropping gate stalls the tee — revisit later).

## Roadmap / next steps

- [ ] Live preview while recording (tee — needs a working gating strategy)
- [ ] Device hot-plug detection (poll `/dev/video*` / inotify)
- [ ] frei0r effects (install `frei0r-plugins` + `gst-plugins-bad`)
- [ ] Video thumbnails in the gallery (decode a frame via a short GStreamer pipeline)
- [ ] Thumbnails in the gallery (photos)
- [ ] Audio in recordings (PipeWire/pulse audio capture)
- [ ] AppStream metainfo + AUR PKGBUILD
- [ ] i18n

## Troubleshooting

- **"Could not open the camera"** — check you're in the `video` group, the
  device exists (`ls /dev/video*`), and no other app holds it open.
- **Missing GStreamer plugins** — `x264enc` comes from `gst-plugins-ugly`;
  `v4l2src` from `gst-plugins-good`.
- **No audio in recordings (planned)** — the current MVP records video only;
  audio capture (PipeWire/pulse) is a follow-up.

## License

MIT — see [LICENSE](LICENSE).
