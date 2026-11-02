#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
publish=$(realpath "$1")
output=$(realpath -m "$2")
version=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/PipeWizard.csproj")
if [[ -z "$version" ]]; then
    echo "Could not read the app version from PipeWizard.csproj." >&2
    exit 1
fi
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

mkdir -p "$work"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
cp -a "$publish" "$work/SOURCES/publish"
cp "$root/packaging/linux/pipewizard.desktop" "$work/SOURCES/"
cp "$root/Assets/pipewizard.png" "$work/SOURCES/"
cp "$root/packaging/linux/pipewizard-launcher" "$work/SOURCES/"

rpmbuild -bb \
    --define "_topdir $work" \
    --define "pipewizard_version $version" \
    "$root/packaging/linux/pipewizard.spec"

mkdir -p "$(dirname "$output")"
cp "$work/RPMS/x86_64/PipeWizard-"*.rpm "$output"
printf 'Created %s\n' "$output"
