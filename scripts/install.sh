#!/usr/bin/env bash
#
# Installs camonlinux for the current user.
#
# Run it from an unpacked release directory (the tarball from a GitHub release),
# where the binary and the share/ tree sit next to this script:
#
#   tar -xzf camonlinux-0.1.0-linux-x64.tar.gz
#   cd camonlinux-0.1.0-linux-x64
#   ./install.sh
#
# Re-running it upgrades in place. `./install.sh --uninstall` removes everything
# it installed and leaves your settings and captures alone.
#
set -euo pipefail

SRC_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${XDG_DATA_HOME:-$HOME/.local}"   # overridden by --prefix
UNINSTALL=0

die() { printf 'error: %s\n' "$*" >&2; exit 1; }
info() { printf '\n%s\n' "$*"; }

usage() {
    sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'EOF'

Options:
  --prefix DIR    Install under DIR (default: ~/.local, i.e. ~/.local/bin and
                  ~/.local/share). Use --prefix /usr/local for a system-wide
                  install (needs write access).
  --uninstall     Remove a previous install.
  -h, --help      Show this help.
EOF
    exit 0
}

while [ $# -gt 0 ]; do
    case "$1" in
        --prefix)    PREFIX="${2:-}"; shift 2 ;;
        --uninstall) UNINSTALL=1; shift ;;
        -h|--help)   usage ;;
        *)           die "unknown option: $1 (try --help)" ;;
    esac
done

[ -n "$PREFIX" ] || die "--prefix needs a directory"
PREFIX="${PREFIX%/}"

BIN_DIR="$PREFIX/bin"
SHARE_DIR="$PREFIX/share"
BINARY="camonlinux"

# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

# GStreamer is a hard runtime requirement for anything involving the camera, and
# it is deliberately not bundled (see the header of scripts/build-release.sh).
# Detect it up front: a warning here beats the user discovering it as an in-app
# error.
gst_packages() {
    # shellcheck disable=SC1091
    local id_like
    id_like="$(. /etc/os-release 2>/dev/null && printf '%s %s' "${ID:-}" "${ID_LIKE:-}")"
    case "$id_like" in
        *arch*|*endeavour*)  echo "gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-plugins-ugly v4l-utils" ;;
        *debian*|*ubuntu*)   echo "gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly v4l-utils" ;;
        *fedora*|*rhel*)     echo "gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugins-bad-free gstreamer1-plugins-ugly-free v4l-utils" ;;
        *suse*)              echo "gstreamer-plugins-base gstreamer-plugins-good gstreamer-plugins-bad gstreamer-plugins-ugly v4l-utils" ;;
        *)                   echo "the GStreamer base/good/bad/ugly plugin sets, and v4l-utils" ;;
    esac
}

have_gstreamer() {
    # Deliberately not `ldconfig -p | grep -q`: grep -q exits at the first match,
    # which kills ldconfig with SIGPIPE, and with `set -o pipefail` the pipeline
    # then reports failure — so finding GStreamer early in the list looked
    # exactly like not having it at all. Capture the output and match that.
    local libs=""
    if command -v ldconfig >/dev/null 2>&1; then
        libs="$(ldconfig -p 2>/dev/null || true)"
    fi
    case "$libs" in
        *libgstreamer-1.0.so.0*) return 0 ;;
    esac
    # No ldconfig, or an empty cache (common in minimal containers).
    ls /usr/lib*/libgstreamer-1.0.so.0 /usr/lib/*/libgstreamer-1.0.so.0 >/dev/null 2>&1
}

refresh_caches() {
    command -v update-desktop-database >/dev/null 2>&1 &&
        update-desktop-database "$SHARE_DIR/applications" 2>/dev/null || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 &&
        gtk-update-icon-cache -f -t "$SHARE_DIR/icons/hicolor" 2>/dev/null || true
}

# --------------------------------------------------------------------------
# Uninstall
# --------------------------------------------------------------------------

if [ "$UNINSTALL" -eq 1 ]; then
    info "Removing camonlinux from $PREFIX"
    removed=0
    for path in \
        "$BIN_DIR/$BINARY" \
        "$SHARE_DIR/applications/camonlinux.desktop" \
        "$SHARE_DIR/metainfo/io.github.amuza2.camonlinux.metainfo.xml" \
        "$SHARE_DIR/icons/hicolor/scalable/apps/camonlinux.svg" \
        "$SHARE_DIR/icons/hicolor/48x48/apps/camonlinux.png" \
        "$SHARE_DIR/icons/hicolor/64x64/apps/camonlinux.png" \
        "$SHARE_DIR/icons/hicolor/128x128/apps/camonlinux.png" \
        "$SHARE_DIR/icons/hicolor/256x256/apps/camonlinux.png" \
        "$SHARE_DIR/icons/hicolor/512x512/apps/camonlinux.png"
    do
        if [ -e "$path" ]; then
            rm -f "$path"
            printf '  removed %s\n' "$path"
            removed=$((removed + 1))
        fi
    done

    refresh_caches
    printf '\nRemoved %d file(s).\n' "$removed"
    printf 'Your settings (%s) and captures are untouched.\n' \
        "${XDG_CONFIG_HOME:-$HOME/.config}/camonlinux"
    exit 0
fi

# --------------------------------------------------------------------------
# Preflight
# --------------------------------------------------------------------------

[ -f "$SRC_DIR/$BINARY" ] || die "cannot find '$BINARY' next to this script — run it from an unpacked release directory"
[ -d "$SRC_DIR/share" ] || die "cannot find the 'share' directory next to this script — run it from an unpacked release directory"

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------

info "Installing camonlinux into $PREFIX"

mkdir -p "$BIN_DIR" \
    "$SHARE_DIR/applications" \
    "$SHARE_DIR/metainfo" \
    "$SHARE_DIR/icons/hicolor/scalable/apps"

install -m 755 "$SRC_DIR/$BINARY" "$BIN_DIR/$BINARY"
printf '  %s\n' "$BIN_DIR/$BINARY"

install -m 644 "$SRC_DIR/share/applications/camonlinux.desktop" \
    "$SHARE_DIR/applications/camonlinux.desktop"
install -m 644 "$SRC_DIR/share/metainfo/io.github.amuza2.camonlinux.metainfo.xml" \
    "$SHARE_DIR/metainfo/io.github.amuza2.camonlinux.metainfo.xml"
install -m 644 "$SRC_DIR/share/icons/hicolor/scalable/apps/camonlinux.svg" \
    "$SHARE_DIR/icons/hicolor/scalable/apps/camonlinux.svg"

# Desktop environments pick the icon closest to the size they need, so install
# the whole ladder rather than one file stretched by the theme.
for size in 48 64 128 256 512; do
    src="$SRC_DIR/share/icons/hicolor/${size}x${size}/apps/camonlinux.png"
    [ -f "$src" ] || continue
    mkdir -p "$SHARE_DIR/icons/hicolor/${size}x${size}/apps"
    install -m 644 "$src" "$SHARE_DIR/icons/hicolor/${size}x${size}/apps/camonlinux.png"
done
printf '  %s\n' "$SHARE_DIR/applications/camonlinux.desktop"
printf '  %s\n' "$SHARE_DIR/icons/hicolor/*/apps/camonlinux.*"

refresh_caches

# --------------------------------------------------------------------------
# Post-install notes
# --------------------------------------------------------------------------

printf '\nInstalled.\n'

if ! have_gstreamer; then
    printf '\n'
    printf '!! GStreamer was not found. camonlinux will start, but the camera will\n'
    printf '   not work until it is installed:\n'
    printf '\n     %s\n' "$(gst_packages)"
    printf '\n'
fi

if ! command -v v4l2-ctl >/dev/null 2>&1; then
    printf '\nNote: v4l2-ctl (v4l-utils) is missing — the camera control sliders\n'
    printf '      will be unavailable.\n'
fi

case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *)
        printf '\n%s is not on your PATH. Add it to your shell profile:\n' "$BIN_DIR"
        printf '\n  export PATH="%s:$PATH"\n' "$BIN_DIR"
        ;;
esac

printf '\nRun "%s" or pick camonlinux from your application menu.\n' "$BINARY"
