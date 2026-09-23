#!/usr/bin/env bash
#
# camonlinux release build.
#
# Produces, under artifacts/ by default:
#
#   camonlinux-<version>-<rid>/            staged release directory
#   camonlinux-<version>-<rid>.tar.gz      the same directory, tarred
#   camonlinux-<version>-<rid>.tar.gz.sha256
#   camonlinux-<version>-<arch>.AppImage   (skipped with --no-appimage)
#   camonlinux-<version>-<arch>.AppImage.sha256
#
# The .NET runtime is bundled, so neither artifact needs `dotnet-runtime`
# installed. GStreamer is NOT bundled, deliberately: the app drives the host's
# camera (v4l2), audio server (PipeWire/Pulse) and desktop session (xdg-open,
# notify-send, canberra). Shipping a second copy of those inside the bundle is
# how you get "no camera found" reports that only reproduce on other people's
# machines, because the bundled copy cannot see the host's devices or sockets.
# A missing GStreamer is detected at startup and reported in the UI rather than
# crashing — see GStreamerCaptureService.InitializeAsync. install.sh checks for
# GStreamer up front and tells the user which packages to install.
#
# Usage: scripts/build-release.sh [options]
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/camonlinux/camonlinux.csproj"

OUTPUT_DIR="$REPO_ROOT/artifacts"
ARCHS="linux-x64"
VERSION=""
SINGLE_FILE=1
COMPRESS=1
BUILD_APPIMAGE=1

die() { printf 'error: %s\n' "$*" >&2; exit 1; }
info() { printf '\n==> %s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }

usage() {
    sed -n '2,22p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'EOF'

Options:
  -v, --version VER      Version to build (default: nearest git tag, else the
                         <Version> in the csproj). Passed to the compiler, so the
                         About window matches the artifact name.
  -a, --archs "RID ..."  Runtime identifiers to publish. Default: linux-x64.
                         e.g. --archs "linux-x64 linux-arm64"
  -o, --output DIR       Output directory (default: artifacts/).
      --no-appimage      Skip the AppImage (it is built for the host
                         architecture only, and needs the network once to fetch
                         appimagetool).
      --no-single-file   Publish a directory of files instead of one binary.
                         Faster startup, but many files to install.
      --no-compress      Disable single-file compression. Larger download,
                         nothing extracted on first run.
  -h, --help             Show this help.
EOF
    exit 0
}

while [ $# -gt 0 ]; do
    case "$1" in
        -v|--version)     VERSION="${2:-}"; shift 2 ;;
        -a|--archs)       ARCHS="${2:-}"; shift 2 ;;
        -o|--output)      OUTPUT_DIR="${2:-}"; shift 2 ;;
        --no-appimage)    BUILD_APPIMAGE=0; shift ;;
        --no-single-file) SINGLE_FILE=0; shift ;;
        --no-compress)    COMPRESS=0; shift ;;
        -h|--help)        usage ;;
        *)                die "unknown option: $1 (try --help)" ;;
    esac
done

command -v dotnet >/dev/null 2>&1 || die "dotnet is not on PATH"

csproj_version() {
    sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT" | head -n1
}

SHA256SUM=""
if command -v sha256sum >/dev/null 2>&1; then SHA256SUM="sha256sum"
elif command -v shasum >/dev/null 2>&1; then SHA256SUM="shasum -a 256"
fi

# Written from inside the output directory so the file lists the artifact's bare
# name and `sha256sum -c` works from wherever it was downloaded to.
write_checksum() {
    [ -n "$SHA256SUM" ] || return 0
    local dir name
    dir="$(dirname "$1")"
    name="$(basename "$1")"
    ( cd "$dir" && $SHA256SUM "$name" > "$name.sha256" )
}

CSPROJ_VERSION="$(csproj_version)"

if [ -z "$VERSION" ]; then
    tag="$(git -C "$REPO_ROOT" describe --tags --abbrev=0 2>/dev/null || true)"
    if [ -n "$tag" ]; then
        VERSION="${tag#v}"
    else
        VERSION="$CSPROJ_VERSION"
        warn "no git tag found; using the csproj version $VERSION"
    fi
fi

[ -n "$VERSION" ] || die "could not determine a version; pass --version"

info "Building camonlinux $VERSION"

# The csproj <Version> is what a plain `dotnet build` reports in the About
# window, while a tagged release is what users downloaded. -p:Version overrides
# it for this build so the two agree; this warns when the repo has drifted.
if [ "$VERSION" != "$CSPROJ_VERSION" ]; then
    warn "version $VERSION differs from <Version>$CSPROJ_VERSION</Version> in the csproj"
fi

# A release is normally built at a tag. If it is not, the artifact name still
# claims the nearest tag's version, so say so rather than shipping a mislabelled
# binary.
if command -v git >/dev/null 2>&1 && git -C "$REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
    if ! git -C "$REPO_ROOT" describe --tags --exact-match >/dev/null 2>&1; then
        warn "HEAD is not exactly at a tag — this is an unreleased revision"
    fi
    if [ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]; then
        warn "the working tree has uncommitted changes"
    fi
fi

# --------------------------------------------------------------------------
# Publish
# --------------------------------------------------------------------------

stage_release_files() {
    local stage="$1"

    install -Dm644 "$REPO_ROOT/LICENSE" "$stage/LICENSE"
    install -Dm644 "$REPO_ROOT/README.md" "$stage/README.md"
    install -Dm755 "$SCRIPT_DIR/install.sh" "$stage/install.sh"

    install -Dm644 "$REPO_ROOT/packaging/camonlinux.desktop" \
        "$stage/share/applications/camonlinux.desktop"
    install -Dm644 "$REPO_ROOT/packaging/io.github.amuza2.camonlinux.metainfo.xml" \
        "$stage/share/metainfo/io.github.amuza2.camonlinux.metainfo.xml"

    # The SVG is the master; the PNGs are pre-rendered at build-the-repo time on
    # purpose, so building a release does not require rsvg-convert or ImageMagick.
    install -Dm644 "$REPO_ROOT/camonlinux/Assets/webcam.svg" \
        "$stage/share/icons/hicolor/scalable/apps/camonlinux.svg"
    for size in 48 64 128 256 512; do
        install -Dm644 "$REPO_ROOT/packaging/icons/camonlinux-$size.png" \
            "$stage/share/icons/hicolor/${size}x${size}/apps/camonlinux.png"
    done
}

publish() {
    local rid="$1" stage="$2"

    local publish_args=(
        --nologo -v minimal
        -c Release
        -r "$rid"
        --self-contained true
        -o "$stage"
        # The About window reads its version from the assembly, so it has to be
        # the release version, not the csproj default.
        -p:Version="$VERSION"
        # No .pdb in a user-facing artifact; the source and the tag are on GitHub.
        -p:DebugType=none
        # Trimming must stay off: Avalonia resolves views and styles by
        # reflection at runtime, and a trimmed build loses them silently (blank
        # windows, missing styles) rather than failing the build.
        -p:PublishTrimmed=false
    )

    if [ "$SINGLE_FILE" -eq 1 ]; then
        publish_args+=(
            -p:PublishSingleFile=true
            # Required on Linux: without it the native SkiaSharp/HarfBuzz
            # libraries stay as loose files next to the executable, which defeats
            # the point of a single-file publish.
            -p:IncludeNativeLibrariesForSelfExtract=true
        )
        if [ "$COMPRESS" -eq 1 ]; then
            # Roughly halves the download. The bundle is unpacked once into
            # DOTNET_BUNDLE_EXTRACT_BASE_DIR (see packaging/AppRun), so only the
            # first launch pays for it.
            publish_args+=(-p:EnableCompressionInSingleFile=true)
        fi
    fi

    dotnet publish "$PROJECT" "${publish_args[@]}"
}

mkdir -p "$OUTPUT_DIR"

STAGES=()
for rid in $ARCHS; do
    info "Publishing $rid"
    stage="$OUTPUT_DIR/camonlinux-$VERSION-$rid"
    rm -rf "$stage"
    mkdir -p "$stage"

    publish "$rid" "$stage"

    [ -x "$stage/camonlinux" ] || die "publish did not produce $stage/camonlinux"

    stage_release_files "$stage"

    tarball="$stage.tar.gz"
    rm -f "$tarball"
    tar -czf "$tarball" -C "$OUTPUT_DIR" "$(basename "$stage")"
    write_checksum "$tarball"

    printf '\n'
    ls -lh "$tarball"
    STAGES+=("$stage")
done

# --------------------------------------------------------------------------
# AppImage (host architecture only)
# --------------------------------------------------------------------------

if [ "$BUILD_APPIMAGE" -eq 1 ]; then
    case "$(uname -m)" in
        x86_64|amd64)  host_rid="linux-x64" ;;
        aarch64|arm64) host_rid="linux-arm64" ;;
        *)             host_rid="" ;;
    esac

    case " $ARCHS " in
        *" $host_rid "*)
            info "Building AppImage"
            "$SCRIPT_DIR/build-appimage.sh" \
                --binary "$OUTPUT_DIR/camonlinux-$VERSION-$host_rid" \
                --version "$VERSION" \
                --output "$OUTPUT_DIR"
            ;;
        *)
            if [ -n "$host_rid" ]; then
                warn "not building an AppImage: this host is $host_rid, which is not in --archs \"$ARCHS\""
            else
                warn "not building an AppImage: unsupported host architecture $(uname -m)"
            fi
            ;;
    esac
fi

info "Done"
ls -lh "$OUTPUT_DIR" | grep -v '^total'
