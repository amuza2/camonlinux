# camonlinux

A simple, modern webcam app for Linux — take photos and record videos with your
webcam. Built with **C# / .NET 10**, **Avalonia UI**, **CommunityToolkit.Mvvm**
and **FluentAvalonia**, with a **GStreamer** capture backend.

Inspired by KDE's Kamoso, but written from scratch in C#.

## Features (MVP)

- Live webcam preview (GStreamer `v4l2src` → `appsink`, rendered on a `WriteableBitmap`)
- Take photos (JPEG, saved to `~/Pictures`)
- **Burst mode** — take a photo every 2.5 s
- **Countdown self-timer** — 3 s / 10 s countdown before a photo (Kamoso-style), with a big on-screen counter; clicking again cancels
- **Effects gallery** — 25 built-in GStreamer effects plus up to 17 **frei0r** filters (cartoon, posterize, pixelate, RGB split, glitch, …) applied to preview, photos and recordings. Effects are auto-detected at startup — the frei0r ones appear automatically once `frei0r-plugins` is installed. Each effect has a **live thumbnail preview** rendered from a camera frame
- Record videos (H.264 + AAC, Matroska `.mkv`, saved to `~/Videos`, with on-screen timer)
- **Audio in recordings** — captures the default microphone (PipeWire/Pulse/ALSA via `autoaudiosrc`) as AAC, with a **Mic** toggle for live mute (applies even mid-recording)
- **Resolution / FPS selector** — a per-camera dropdown of the modes the device actually supports (e.g. 1920×1080 @ 30, 1280×720 @ 30, 640×480 @ 30), detected from the device caps. High-res modes use the camera's MJPEG stream + `jpegdec`, since many UVC cams (incl. the C930e) can't do raw 720p/1080p
- **Record quality + auto-split** — Low/Med/High `x264enc` bitrate, and an optional size cap that splits long recordings into numbered parts (`video_…-1.mkv`, `video_…-2.mkv`) without stopping the preview
- **Rotation & digital zoom** — 90°/180°/270° rotation (for sideways-mounted cams) and up to 4× smooth digital zoom (crop + `videoscale`), applied to the preview, photos and recordings
- Mirror toggle
- Camera selection with friendly names (e.g. "Logitech Webcam C930e" — read from sysfs, deduped to real capture nodes)
- **Device hot-plug detection** — the camera list refreshes automatically every 2 s; plug in a camera and it appears (and can auto-start), unplug the active one and it switches to another / stops gracefully
- Recent captures gallery with **photo & video thumbnails** and delete (moves to trash)
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

# Optional: extra (frei0r) effects — cartoon, night vision, pixelate, …
sudo pacman -S frei0r-plugins

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

A single GStreamer pipeline drives both preview and recording:

```
# Preview (idle)
v4l2src {+ selected mode caps; + jpegdec for MJPEG} ! videoconvert ! videoflip (mirror) ! {effect} ! videoconvert ! video/x-raw,format=BGRx ! appsink

# Recording (pipeline rebuilt with a tee; preview stays LIVE)
v4l2src {+ mode caps} ! videoconvert ! videoflip ! {effect} ! tee
    ├─ queue ! videoconvert ! video/x-raw,format=BGRx ! appsink      → live preview
    └─ queue ! x264enc (bitrate by quality) ! matroskamux ! filesink   → MKV recording
       autoaudiosrc ! volume(mute) ! fdkaacenc ! mux.                  → mic audio (AAC)
```

- Frames are pulled from the `appsink` on a background thread (`TryPullSample`)
  and rendered on the preview `WriteableBitmap`.
- Starting a recording rebuilds the pipeline to add the record branch, so the
  live preview keeps running while recording.
- Stopping sends EOS down the record branch only — the Matroska container is
  finalized properly — then rebuilds back to the plain preview.
- When a size cap is set, the recording file is watched and, once it exceeds the
  cap, finalized and continued into a numbered next file without stopping the
  preview.

## Roadmap / next steps

- [x] Device hot-plug detection (poll `/dev/video*` every 2 s)
- [x] frei0r effects (install `frei0r-plugins` + `gst-plugins-bad`) — auto-detected
- [x] Audio in recordings (default mic → AAC; `Mic` toggle mutes live)
- [x] Resolution / FPS selector (per-camera; MJPEG for high-res modes)
- [x] Record quality (Low/Med/High) + auto-split at a size cap
- [x] Countdown self-timer (3 s / 10 s before a photo)
- [x] Rotation (90°/180°/270°) + digital zoom (up to 4×)
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
