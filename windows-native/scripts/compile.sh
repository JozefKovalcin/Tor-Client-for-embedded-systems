#!/usr/bin/env bash
# Double-click entry point: dependency setup followed by the optimized build.
set -Eeuo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if [[ -z ${TOR_WINDOWS_WORK:-} && -n ${TOR_WINDOWS_WORK_WIN:-} ]]; then
    export TOR_WINDOWS_WORK
    TOR_WINDOWS_WORK=$(cygpath -u "$TOR_WINDOWS_WORK_WIN")
fi
export PROFILE=optimized LINKAGE=static
case ${1:-} in
    --help|--check-deps) exec bash "$root/build-windows.sh" "$@" ;;
    '') ;;
    *) echo 'Usage: COMPILE.cmd [--check-deps|--help]' >&2; exit 2 ;;
esac
echo 'Checking MSYS2 UCRT64 build dependencies...'
if ! bash "$root/build-windows.sh" --check-deps; then
    echo 'Installing the required build tools into this MSYS2 installation...'
    if ! pacman -S --needed --noconfirm base-devel make perl python curl tar patch diffutils \
        mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-pkgconf; then
        echo 'Dependency installation failed. Open MSYS2 UCRT64, run pacman -Syu,' >&2
        echo 'reopen/update the terminal as requested, then run COMPILE.cmd again.' >&2
        exit 1
    fi
    bash "$root/build-windows.sh" --check-deps
fi
exec bash "$root/build-windows.sh"
