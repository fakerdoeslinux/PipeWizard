#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
publish=$(realpath "$1")
output=$(realpath -m "$2")
appimagetool=${APPIMAGETOOL:-}
if [[ -z "$appimagetool" || ! -x "$appimagetool" ]]; then
    echo "Set APPIMAGETOOL to an executable appimagetool binary." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
appdir="$work/PipeWizard.AppDir"
mkdir -p "$appdir/usr/lib/pipewizard" "$appdir/usr/share/applications" \
    "$appdir/usr/share/icons/hicolor/256x256/apps"
cp -a "$publish"/. "$appdir/usr/lib/pipewizard/"
sed 's/^Exec=pipewizard$/Exec=AppRun/' \
    "$root/packaging/linux/pipewizard.desktop" > "$appdir/usr/share/applications/pipewizard.desktop"
cp "$appdir/usr/share/applications/pipewizard.desktop" "$appdir/pipewizard.desktop"
cp "$root/Assets/pipewizard.png" "$appdir/usr/share/icons/hicolor/256x256/apps/pipewizard.png"
cp "$root/Assets/pipewizard.png" "$appdir/pipewizard.png"

cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
HERE=$(dirname "$(readlink -f "$0")")
exec "$HERE/usr/lib/pipewizard/PipeWizard" "$@"
EOF
chmod 0755 "$appdir/AppRun"

mkdir -p "$(dirname "$output")"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" "$appdir" "$output"
printf 'Created %s\n' "$output"
