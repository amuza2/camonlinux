#!/usr/bin/env bash
#
# Builds a single-file AppImage from an already-published camonlinux release.
#
# Normally called by scripts/build-release.sh; it can also be run on its own
# against any publish directory:
#
#   scripts/build-appimage.sh --binary artifacts/camonlinux-0.1.0-linux-x64
#
# appimagetool is not part of most distributions, so it is downloaded on demand
# (see fetch_appimagetool) unless --appimagetool points at a local copy.
#
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

VERSION=""
ARCH=""
BINARY_DIR=""
OUTPUT_DIR="$REPO_ROOT/artifacts"
APPIMAGETOOL=""
TOOL_CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/camonlinux"

die() { printf 'error: %s\n' "$*" >&2; exit 1; }
info() { printf '\n==> %s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }

usage() {
    sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'EOF'

Options:
  --binary DIR          Published app directory to package (required).
  -v, --version VER     Version to label the AppImage with (default: from the
                        csproj, or the nearest git tag).
  -a, --arch ARCH       AppImage architecture: x86_64 or aarch64.
                        Default: the host architecture.
  -o, --output DIR      Where to write the .AppImage (default: artifacts/).
      --appimagetool P  Use this appimagetool instead of downloading one.
      --tool-cache DIR  Where to cache the downloaded appimagetool.
  -h, --help            Show this help.
EOF
    exit 0
}

while [ $# -gt 0 ]; do
    case "$1" in
        --binary)        BINARY_DIR="${2:-}"; shift 2 ;;
        -v|--version)    VERSION="${2:-}"; shift 2 ;;
        -a|--arch)       ARCH="${2:-}"; shift 2 ;;
        -o|--output)     OUTPUT_DIR="${2:-}"; shift 2 ;;
        --appimagetool)  APPIMAGETOOL="${2:-}"; shift 2 ;;
        --tool-cache)    TOOL_CACHE="${2:-}"; shift 2 ;;
        -h|--help)       usage ;;
        *)               die "unknown option: $1 (try --help)" ;;
    esac
done

[ -n "$BINARY_DIR" ] || die "--binary DIR is required (try --help)"
[ -d "$BINARY_DIR" ] || die "not a directory: $BINARY_DIR"

host_arch() {
    case "$(uname -m)" in
        x86_64|amd64)   echo x86_64 ;;
        aarch64|arm64)  echo aarch64 ;;
        *)              echo unknown ;;
    esac
}

[ -n "$ARCH" ] || ARCH="$(host_arch)"
case "$ARCH" in
    x86_64|aarch64) ;;
    *) die "unsupported architecture '$ARCH' — appimagetool ships x86_64/aarch64/i686/armhf only" ;;
esac

csproj_version() {
    sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$REPO_ROOT/camonlinux/camonlinux.csproj" | head -n1
}

if [ -z "$VERSION" ]; then
    tag="$(git -C "$REPO_ROOT" describe --tags --abbrev=0 2>/dev/null || true)"
    if [ -n "$tag" ]; then VERSION="${tag#v}"; else VERSION="$(csproj_version)"; fi
fi

# --------------------------------------------------------------------------
# appimagetool
# --------------------------------------------------------------------------

# appimagetool has no versioned releases — "continuous" is the only tag, and its
# assets are rebuilt in place. Pinning a hash here would therefore break every
# release build whenever upstream rebuilds, so instead the digest published by
# the GitHub API is fetched and the download is checked against it. That catches
# a truncated or substituted download; pass APPIMAGETOOL_SHA256 to pin exactly,
# or --appimagetool to use a binary you already trust.
appimagetool_digest() {
    local asset="$1"
    command -v python3 >/dev/null 2>&1 || return 0
    curl -fsSL -m 30 \
        "https://api.github.com/repos/AppImage/appimagetool/releases/tags/continuous" 2>/dev/null \
        | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
want = sys.argv[1]
for asset in data.get("assets", []):
    if asset.get("name") == want:
        digest = asset.get("digest") or ""
        print(digest.split(":", 1)[1] if ":" in digest else "")
        break
' "$asset" 2>/dev/null || true
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
    elif command -v shasum >/dev/null 2>&1; then shasum -a 256 "$1" | cut -d' ' -f1
    else openssl dgst -sha256 "$1" | awk '{print $NF}'
    fi
}

fetch_appimagetool() {
    local asset="appimagetool-${ARCH}.AppImage"
    local dest="$TOOL_CACHE/$asset"
    local url="https://github.com/AppImage/appimagetool/releases/download/continuous/$asset"

    if [ ! -x "$dest" ]; then
        # Progress goes to stderr: this function's stdout is its return value.
        printf '\n==> Downloading %s\n' "$asset" >&2
        mkdir -p "$TOOL_CACHE"
        # Download beside the target so a failed run never leaves a half-written
        # binary that the next run would happily try to execute.
        curl -fL --retry 3 --connect-timeout 20 -o "$dest.part" "$url" \
            || die "could not download $url"
        [ -s "$dest.part" ] || { rm -f "$dest.part"; die "downloaded $asset is empty"; }
        mv "$dest.part" "$dest"
        chmod +x "$dest"
    fi

    local expected="${APPIMAGETOOL_SHA256:-}"
    [ -n "$expected" ] || expected="$(appimagetool_digest "$asset")"
    if [ -n "$expected" ]; then
        local actual
        actual="$(sha256_of "$dest")"
        [ "$actual" = "$expected" ] \
            || die "$asset checksum mismatch
  expected $expected
  actual   $actual
Delete $dest and retry, or pass --appimagetool with your own copy."
    else
        warn "could not fetch the published checksum for $asset; using it unverified"
        warn "sha256($asset) = $(sha256_of "$dest")"
    fi

    printf '%s' "$dest"
}

if [ -z "$APPIMAGETOOL" ]; then
    APPIMAGETOOL="$(fetch_appimagetool)"
fi
[ -x "$APPIMAGETOOL" ] || die "appimagetool is not executable: $APPIMAGETOOL"

# appimagetool is itself an AppImage and needs FUSE to mount itself. Containers
# and CI runners usually lack it, so probe once and fall back to the built-in
# extract-and-run mode rather than failing with a confusing mount error.
appimagetool_cmd() {
    if "$APPIMAGETOOL" --version >/dev/null 2>&1; then
        "$APPIMAGETOOL" "$@"
    else
        "$APPIMAGETOOL" --appimage-extract-and-run "$@"
    fi
}

# --------------------------------------------------------------------------
# AppDir
# --------------------------------------------------------------------------

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APPDIR="$WORK_DIR/camonlinux.AppDir"
info "Building AppDir for $ARCH"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share"

# The app itself. A self-contained single-file publish is a lone executable, but
# stay tolerant of a framework-dependent or non-single-file layout so this script
# keeps working if the publish flags change.
if [ -f "$BINARY_DIR/camonlinux" ]; then
    cp "$BINARY_DIR/camonlinux" "$APPDIR/usr/bin/camonlinux"
else
    die "no 'camonlinux' executable in $BINARY_DIR — publish it first"
fi
chmod 755 "$APPDIR/usr/bin/camonlinux"

# Desktop integration files. Take them from the release directory when present
# (build-release.sh stages them there) so a packaged release is self-describing,
# and fall back to the repo for a raw publish directory.
copy_tree() {
    local from="$1" to="$2"
    [ -d "$from" ] || return 0
    cp -a "$from/." "$to/"
}

if [ -d "$BINARY_DIR/share" ]; then
    copy_tree "$BINARY_DIR/share" "$APPDIR/usr/share"
else
    install -Dm644 "$REPO_ROOT/packaging/camonlinux.desktop" \
        "$APPDIR/usr/share/applications/camonlinux.desktop"
    install -Dm644 "$REPO_ROOT/packaging/io.github.amuza2.camonlinux.metainfo.xml" \
        "$APPDIR/usr/share/metainfo/io.github.amuza2.camonlinux.metainfo.xml"
    install -Dm644 "$REPO_ROOT/camonlinux/Assets/webcam.svg" \
        "$APPDIR/usr/share/icons/hicolor/scalable/apps/camonlinux.svg"
    for size in 48 64 128 256 512; do
        install -Dm644 "$REPO_ROOT/packaging/icons/camonlinux-$size.png" \
            "$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps/camonlinux.png"
    done
fi

# appimagetool wants the .desktop file and an icon named after it in the AppDir
# root, and it uses the root desktop file to derive the AppStream id and the
# update information. The name must match Icon= (camonlinux) exactly.
install -Dm644 "$APPDIR/usr/share/applications/camonlinux.desktop" \
    "$APPDIR/camonlinux.desktop"
install -Dm755 "$REPO_ROOT/packaging/AppRun" "$APPDIR/AppRun"
install -Dm644 "$REPO_ROOT/packaging/icons/camonlinux-256.png" "$APPDIR/camonlinux.png"

# A root .DirIcon is what file managers and appimaged show for the AppImage.
cp "$APPDIR/camonlinux.png" "$APPDIR/.DirIcon"

# --------------------------------------------------------------------------
# Assemble
# --------------------------------------------------------------------------

mkdir -p "$OUTPUT_DIR"
OUT="$OUTPUT_DIR/camonlinux-$VERSION-$ARCH.AppImage"
rm -f "$OUT"

info "Running appimagetool"
# ARCH tells appimagetool which runtime to embed; the AppDir contents must
# already match it (appimagetool cannot cross-compile an application).
ARCH="$ARCH" NO_STRIP="${NO_STRIP:-1}" \
    appimagetool_cmd "$APPDIR" "$OUT" \
    || die "appimagetool failed"

chmod 755 "$OUT"

# The release dir's checksum helper, reused so both scripts agree on the format.
printf '%s  %s\n' "$(sha256_of "$OUT")" "$(basename "$OUT")" > "$OUT.sha256"

printf '\n'
info "Built $(basename "$OUT")"
ls -lh "$OUT"
