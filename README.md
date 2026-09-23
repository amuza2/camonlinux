<div align="center">

![camonlinux](packaging/icons/camonlinux-128.png)

# camonlinux

[![Build](https://github.com/amuza2/camonlinux/actions/workflows/build.yml/badge.svg)](https://github.com/amuza2/camonlinux/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/) [![Platform](https://img.shields.io/badge/Platform-Linux-orange)](https://www.linux.org/) [![Ko-fi](https://img.shields.io/badge/Ko--fi-Support-FF5E5B?logo=ko-fi)](https://ko-fi.com/codingisamazing)

A simple, modern webcam app for Linux, built with Avalonia UI and a GStreamer capture
backend. camonlinux takes photos and records videos with your webcam, with live effects,
background masking and a virtual-camera output.

Inspired by KDE's Kamoso, but written from scratch in **C# / .NET 10**.

<img width="1773" height="1016" alt="image" src="https://github.com/user-attachments/assets/63691fdb-d218-437d-8baf-206dbd8eccf2" />


<!-- Screenshot: drag-and-drop your image into a GitHub comment or issue, copy the
     resulting user-attachments URL into src below, then delete the comment markers.
<img width="817" height="608" alt="camonlinux main window" src="https://github.com/user-attachments/assets/REPLACE-ME" />
-->

</div>

## Features

- Live webcam preview (GStreamer `v4l2src` → `appsink`, rendered on a `WriteableBitmap`)
- Take photos (JPEG or PNG, saved to `~/Pictures`) with a **white screen flash** on capture
- **Burst mode** — take a photo every 2.5 s
- **Countdown self-timer** — 3 s / 10 s countdown before a photo (Kamoso-style), with a big on-screen counter; clicking again cancels
- **Effects gallery** — 25 built-in GStreamer effects plus up to 17 **frei0r** filters (cartoon, posterize, pixelate, RGB split, glitch, …) applied to preview, photos and recordings. Effects are auto-detected at startup — the frei0r ones appear automatically once `frei0r-plugins` is installed. Each effect has a **live thumbnail preview** rendered from a camera frame; **favorite** effects pin to the top (★), and parameterized effects (**Vivid**, **Grayscale**) have an **intensity slider**
- Record videos (H.264 + AAC, Matroska `.mkv`, saved to `~/Videos`, with on-screen timer)
- **Audio in recordings** — captures the default microphone (PipeWire/Pulse/ALSA via `autoaudiosrc`) as AAC, with a **Mic** toggle for live mute (applies even mid-recording)
- **Resolution / FPS selector** — a per-camera dropdown of the modes the device actually supports (e.g. 1920×1080 @ 30, 1280×720 @ 30, 640×480 @ 30), detected from the device caps. High-res modes use the camera's MJPEG stream + `jpegdec`, since many UVC cams (incl. the C930e) can't do raw 720p/1080p
- **Record quality + auto-split** — Low/Med/High `x264enc` bitrate, and an optional size cap that splits long recordings into numbered parts (`video_…-1.mkv`, `video_…-2.mkv`) without stopping the preview
- **Rotation & digital zoom** — 90°/180°/270° rotation (for sideways-mounted cams) and up to 4× smooth digital zoom (crop + `videoscale`), applied to the preview, photos and recordings
- **Live camera controls** — brightness / contrast / saturation sliders that set the v4l2 controls instantly via `v4l2-ctl` (no pipeline rebuild); values persist
- **Timestamp overlay** — a **Stamp** menu toggle burns the date & time into the corner of photos (SkiaSharp) and recordings (`textoverlay`)
- Mirror toggle (Camera ▸ Mirror Image, or `M`)
- Camera selection with friendly names (e.g. "Logitech Webcam C930e" — read from sysfs, deduped to real capture nodes)
- **Device hot-plug detection** — the camera list refreshes automatically every 2 s; plug in a camera and it appears (and can auto-start), unplug the active one and it switches to another / stops gracefully
- Recent captures gallery with **photo & video thumbnails**, **play videos** (system player) and **delete** (to the trash), plus a **settings window** to pick the photo & video folders
- **Menu bar** — File / Camera / View / Tools / Help. The toolbar keeps the two controls worth one click (camera and resolution); mirror, timestamp, mask, virtual webcam, rescan, rotation and zoom live in the menus, where the active value is shown in the submenu title and ticked in the list
- **Status pills** for Mask and Virtual Webcam, so a mode that now lives in a menu (and a live virtual camera in particular) is still visible at a glance
- **About window** (Help ▸ About) with version, links and a copyable diagnostics block
- **Hideable right panel** (View ▸ Captures & Effects Panel)
- **Background masking** — a mask editor with shape, gradient, SVG, chroma-key and
  background-subtraction masks, feathering and per-mask colour adjustment, stacked in
  layers. The result applies to the preview, the photos and the recordings
- **Virtual webcam** — publishes the processed preview to a `v4l2loopback` device so any
  other application (Zoom, OBS, a browser) can use it as a camera
- **Audio device picker** — choose which mic to record (lists PipeWire/Pulse sources via `pactl`, monitors excluded)
- **Mouse-wheel zoom** over the preview, **live recording file size** beside the timer, **effect search box**, **toast notifications**, **delete confirmation**, **auto-refreshing gallery**, and the window **remembers its size/state**
- **Single-instance guard** — a second launch notifies and exits instead of fighting over the camera
- Desktop notifications (`notify-send`)

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Space` | Take a photo |
| `R` | Start / stop recording |
| `B` | Start / stop burst mode |
| `M` | Mirror the camera image |
| `E` | Switch the right panel between Captures and Effects |
| `F11` | Fullscreen |
| `Ctrl+Q` | Quit |

Mouse wheel over the preview zooms, and double-clicking the right panel's resize grip
resets its width.

The same gestures are also shown beside their menu entries. `Window.KeyBindings` is what
actually runs them, so a shortcut has to be changed in both places to stay in step.

## Installation

### Download a release

Tagged releases ship two self-contained artifacts — no `dotnet-runtime` needed:

| Artifact | Use |
|---|---|
| `camonlinux-<version>-x86_64.AppImage` | Single file: make it executable and run it |
| `camonlinux-<version>-linux-x64.tar.gz` | `./install.sh` adds a launcher and icons |

```bash
# AppImage
chmod +x camonlinux-*-x86_64.AppImage
./camonlinux-*-x86_64.AppImage

# Tarball
tar -xzf camonlinux-*-linux-x64.tar.gz
cd camonlinux-*-linux-x64
./install.sh                       # ~/.local (per-user)
./install.sh --prefix /usr/local   # system-wide
./install.sh --uninstall           # remove it again
```

Each artifact has a `.sha256` file beside it.

### Prerequisites

The .NET runtime is bundled; **GStreamer is not**, deliberately — the app has to use *your*
camera (v4l2), audio server (PipeWire/Pulse) and desktop session (`xdg-open`, `notify-send`).
A bundled copy cannot see the host's devices or sockets, so it would only produce "no camera
found" reports on other people's machines.

| Distribution | Packages |
|---|---|
| Arch | `gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-plugins-ugly v4l-utils` |
| Debian/Ubuntu | `gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly v4l-utils` |
| Fedora | `gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugins-bad-free gstreamer1-plugins-ugly-free v4l-utils` |

On Arch / EndeavourOS:

```bash
sudo pacman -S gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-plugins-ugly v4l-utils
sudo usermod -aG video $USER   # webcam access — log out and back in
```

`install.sh` checks for GStreamer and prints the package names above. Missing GStreamer is
not fatal: the app starts and reports it in the UI rather than crashing.

Optional extras: `frei0r-plugins` (extra effects), `libcanberra` (shutter sound),
`libnotify` (desktop notifications), `v4l2loopback-dkms` (virtual webcam).

### Build from source

```bash
git clone https://github.com/amuza2/camonlinux.git
cd camonlinux

dotnet run --project camonlinux
# or
dotnet build
./camonlinux/bin/Debug/net10.0/camonlinux
```

### Tests

The unit tests cover the pure logic — the masking pipeline, the BGRA pixel helpers,
GStreamer launch-string escaping, settings persistence, the trash implementation and the
packaging metadata. They need no camera, no GStreamer and no display, so they run anywhere:

```bash
dotnet test
```

CI (`.github/workflows/build.yml`) runs the build with `-warnaserror`, the test suite and a
publish on every push and pull request.

## Usage

1. **Pick a camera** — the toolbar dropdown. The list refreshes itself as you plug devices
   in and out.
2. **Take a photo** — `Take Photo` (or `Space`). Choose JPEG or PNG and an optional 3 s / 10 s
   self-timer first; the frame is saved to `~/Pictures`.
3. **Record a video** — switch to the `Video` tab and press `Record` (or `R`). Pick a mic, a
   quality and an optional size cap; the file lands in `~/Videos`.
4. **Add effects** — open the `Effects` side of the right panel, search or star a favourite,
   and it applies to the preview, photos and recordings at once.
5. **Review** — the `Captures` side lists recent photos and videos with thumbnails. Play a
   video in your system player, or delete it to the trash.
6. **Go further** — `Tools ▸ Mask Editor` hides your background, and
   `Tools ▸ Virtual Webcam` publishes the result to a `v4l2loopback` device for other apps.

### Supported Formats

- Photos: `.jpg`, `.png`
- Video: `.mkv` (H.264 + AAC)

## Configuration

Settings are stored in `~/.config/camonlinux/settings.json`, and include:

- Camera, resolution, rotation and digital zoom
- Photo directory, video directory, photo format, record quality and size cap
- Camera controls — brightness, contrast, saturation, sharpness, gain, backlight
  compensation, white balance, exposure and focus
- Microphone selection and mute state, mirroring, timestamp overlay, burst interval
- Favourite effects and their saved intensities
- Window geometry, plus the width and visibility of the right panel

## Tech stack

| Piece | Choice |
|---|---|
| Language / runtime | C# / .NET 10 (`net10.0`) |
| UI framework | [Avalonia](https://avaloniaui.net/) 12.1.1 |
| UI theme (no manual styling) | [FluentAvalonia](https://github.com/amwx/FluentAvalonia) 3.0.2 (MIT) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4 |
| Capture backend | GStreamer via [GirCore](https://github.com/gircore/gir.core) 0.8.1 (`GirCore.Gst-1.0` etc.) |
| Photo encoding | [SkiaSharp](https://github.com/mono/SkiaSharp) |
| System | GStreamer 1.20+, `v4l-utils`, a camera at `/dev/video*` |
| License | MIT |

## Releasing

```bash
./scripts/build-release.sh                      # binaries + AppImage, into artifacts/
./scripts/build-release.sh --archs "linux-x64 linux-arm64"
./scripts/build-release.sh --no-appimage        # skip the AppImage
./scripts/build-release.sh --version 0.2.0      # override the version
```

`--version` is compiled in, so the About window matches the artifact name. The version
otherwise comes from the nearest git tag, falling back to `<Version>` in the csproj.

The AppImage is built by `scripts/build-appimage.sh`, which downloads `appimagetool` on
demand (most distributions do not package it) into `~/.cache/camonlinux` and checks it
against the digest GitHub publishes for that asset. Pass `--appimagetool` to use your own.

Notes on the publish flags, in case you change them:

- **Trimming stays off.** Avalonia resolves views, styles and bindings by reflection, and a
  trimmed build loses them *silently* — blank windows, not a build error.
- **Single-file compresses the bundle** (~42 MB instead of ~110 MB). It is unpacked once
  into `~/.cache/camonlinux/dotnet`, so only the first launch pays for it; `packaging/AppRun`
  sets `DOTNET_BUNDLE_EXTRACT_BASE_DIR` so that survives a reboot.
- **`DebugType=none`** keeps `.pdb` files out of user-facing artifacts; source and tag are
  on GitHub.

Pushing a `v*` tag runs `.github/workflows/release.yml`, which builds both artifacts, checks
that they start, and attaches them (with `.sha256` files) to a GitHub release.

The metadata in `packaging/` — desktop file, AppStream metainfo, icon ladder — is asserted by
`camonlinux.Tests/PackagingMetadataTests.cs`. Those are the things that fail *silently* for
users (a blank launcher icon, an app that never shows up in a software centre, an AppImage
that refuses to build), so they are tested like code.

## Project structure

```
camonlinux/
├── Models/                # CameraDevice, MediaItem, AppSettings, MenuChoice/MenuText
├── Services/              # Settings, MediaLibrary (watcher), Trash, Notifications, VirtualCamera
├── Capture/               # ICaptureService + GStreamerCaptureService, CameraFrame, IFrameProcessor
├── Controls/              # VideoSurface (WriteableBitmap renderer)
├── Imaging/               # PixelBuffer (SIMD helpers for BGRA32 buffers)
├── Masking/               # MaskPipeline + MaskFrameProcessor, Effects/, Geometry/, Svg/
├── ViewModels/            # MainWindowViewModel (CommunityToolkit MVVM), MaskEditorViewModel
├── Views/                 # MainWindow.axaml (FluentAvalonia UI) + dialogs
└── Assets/                # app icon (embedded into the binary as a resource)

camonlinux.Tests/          # xUnit tests for the pure logic (no camera/GStreamer needed)
packaging/                 # desktop file, AppStream metainfo, AppRun, icons/, PKGBUILD
scripts/                   # build-release.sh, build-appimage.sh, install.sh
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
- [x] AppStream metainfo + AUR PKGBUILD
- [x] Self-contained release builds — AppImage + tarball, built by CI on a `v*` tag
- [ ] i18n

## Troubleshooting

- **"Could not open the camera"** — check you're in the `video` group, the
  device exists (`ls /dev/video*`), and no other app holds it open.
- **Missing GStreamer plugins** — `x264enc` comes from `gst-plugins-ugly`;
  `v4l2src` from `gst-plugins-good`.
- **Recordings have no sound** — check the **Mic** toggle isn't muting, and that
  the **Audio** dropdown is pointing at the mic you expect (recording goes through
  `autoaudiosrc`, so a source that only exists as a PulseAudio/PipeWire monitor can
  end up silent).
- **`textoverlay`/`jpegdec` not found** — install `gst-plugins-base` and
  `gst-plugins-good` respectively.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome!

1. **Open an issue** first to discuss the proposed change
2. **Fork** the repository
3. **Create a branch** for your feature or fix
4. **Submit a Pull Request** referencing the issue

## Acknowledgments

- Icon by [Flaticon](https://www.flaticon.com/)
- Inspired by [Kamoso](https://apps.kde.org/kamoso/)

<div align="center">
Made with ❤️ for the Linux community
</div>
