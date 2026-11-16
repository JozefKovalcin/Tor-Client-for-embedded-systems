#!/usr/bin/env bash
# Native MSYS2 build only. Each invocation gets a new input-hash output.
set -Eeuo pipefail
main() {
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
die() { echo "ERROR: $*" >&2; exit 1; }
case ${1:-} in
    --help) echo 'Usage: bash build-windows.sh [--check-deps] (MSYS2 UCRT64). See README.'; exit 0 ;;
    ''|--check-deps) ;;
    *) die "Unknown argument: $1" ;;
esac
[[ ${MSYSTEM:-} == UCRT64 ]] || die 'Open the MSYS2 UCRT64 terminal (not MSYS, Git Bash, WSL or MINGW64).'
export PATH=/ucrt64/bin:/usr/bin
for tool in gcc ld ar ranlib windres strip objdump nm dlltool make perl python3 curl tar sha256sum pkg-config patch; do
    command -v "$tool" >/dev/null || die "Missing $tool. Use COMPILE.cmd to install build dependencies."
done
[[ $(command -v gcc) == /ucrt64/bin/gcc ]] || die 'GCC must come from /ucrt64/bin.'
[[ $(gcc -dumpmachine) == x86_64-w64-mingw32 ]] || die 'An x86_64 MinGW-w64 compiler is required.'
python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3, 12) else 1)' || die 'Python 3.12+ is required; update the MSYS2 python package.'
[[ ${1:-} != --check-deps ]] || { echo 'PASS UCRT64 build dependencies'; exit 0; }
linkage=${LINKAGE:-static}
profile=${PROFILE:-optimized}
[[ $linkage == static || $linkage == shared ]] || die 'LINKAGE must be static or shared.'
[[ $profile == optimized || $profile == reference ]] || die 'PROFILE must be optimized or reference.'
work=${TOR_WINDOWS_WORK:-$root/.work}
[[ $work != *[[:space:]]* ]] || die 'Build path contains whitespace. Use a checkout such as C:/src/tor or set TOR_WINDOWS_WORK to a native Windows path without spaces, in MSYS notation.'
mkdir -p "$work"
work=$(cd "$work" && pwd)
cache=${TOR_WINDOWS_CACHE:-$work/downloads}
jobs=${JOBS:-$(nproc)}
[[ $jobs =~ ^[1-9][0-9]*$ ]] || die 'JOBS must be a positive integer.'
export SOURCE_DATE_EPOCH=1792627200 TZ=UTC LC_ALL=C
unset CFLAGS CXXFLAGS CPPFLAGS LDFLAGS LIBS CPATH LIBRARY_PATH C_INCLUDE_PATH OPENSSL_CONF OPENSSL_MODULES
export PKG_CONFIG_LIBDIR=/nonexistent
unset PKG_CONFIG_PATH
input_hash=$({ find "$root" -type f \
    -not -path "$root/.work/*" -not -path "$root/dist*/*" -not -path "$root/evidence/*" \
    -not -path "$root/runtime/*" -not -path '*/__pycache__/*' -print0 | sort -z | xargs -0 sha256sum;
    gcc -v 2>&1; ld --version; pacman -Q; printf '%s\n' "$linkage" "$profile";
    } | sha256sum | cut -c1-20)
run=$work/$input_hash/$(date -u +%Y%m%dT%H%M%SZ)-$$
mkdir -p "$run"
exec > >(tee "$run/build.log") 2>&1
trap 'code=$?; echo "FAIL exit=$code line=$LINENO; preserved evidence: $run" >&2; exit "$code"' ERR
printf 'Native Windows build: %s %s\nEvidence: %s\n' "$profile" "$linkage" "$run"
pacman -Q > "$run/toolchain-packages.txt"
gcc -v 2> "$run/compiler.txt"
cp "$root/source-manifest.json" "$run/source-manifest.json"
mkdir "$run/project"
for input in "$root"/*.md "$root"/*.txt "$root"/*.sh "$root"/*.cmd "$root"/*.json; do
    if [[ -f $input ]]; then cp "$input" "$run/project/"; fi
done
cp -R "$root/config" "$root/scripts" "$run/project/"
[[ ! -d $root/tests ]] || cp -R "$root/tests" "$run/project/"
[[ ! -d $root/two-pc-test ]] || cp -R "$root/two-pc-test" "$run/project/"
[[ ! -d $root/patches ]] || cp -R "$root/patches" "$run/project/"
project=$run/project
python3 "$project/scripts/prepare.py" "$project/source-manifest.json" "$cache" "$run/src"
prefix=$run/prefix
mkdir -p "$prefix"/{lib,include,bin}
export PATH="$prefix/bin:$PATH"
# Export only PE-compatible flags. No -march=native, fast-math, ELF PIE or RELRO.
export CFLAGS="-O2 -g0 -ffunction-sections -fdata-sections -fstack-protector-strong -fno-ident -ffile-prefix-map=$run=."
export LDFLAGS='-Wl,--gc-sections,--dynamicbase,--nxcompat,--high-entropy-va,--no-insert-timestamp -static-libgcc'
export CC=gcc LD=ld AR=ar RANLIB=ranlib WINDRES=windres NM=nm OBJDUMP=objdump DLLTOOL=dlltool
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
shared=0
ssl_mode=no-shared
event_mode=(--disable-shared --enable-static)
tor_link=(--enable-static-libevent --enable-static-openssl --enable-static-zlib)
if [[ $linkage == shared ]]; then
    shared=1; ssl_mode=shared
    event_mode=(--enable-shared --disable-static)
    tor_link=()
fi
(
    cd "$run/src/zlib-1.3.2"
    # Strip only staged distribution files, after linker/AV handles are released.
    make -f win32/Makefile.gcc -j1 STRIP=: CFLAGS="$CFLAGS" LDFLAGS="$LDFLAGS"
    make -f win32/Makefile.gcc test testdll
    make -f win32/Makefile.gcc install SHARED_MODE="$shared" \
        BINARY_PATH="$prefix/bin" INCLUDE_PATH="$prefix/include" LIBRARY_PATH="$prefix/lib"
)
(
    cd "$run/src/openssl-3.6.5"
    # Default provider is built in. No cipher/curve/TLS/entropy reductions.
    perl Configure mingw64 "$ssl_mode" no-tests --prefix="$(cygpath -m "$prefix")" --libdir=lib $CFLAGS $LDFLAGS
    make -j"$jobs"
    make install_sw
)
(
    cd "$run/src/libevent-2.1.13-stable"
    # Let the PE linker validate import libraries rather than libtool's old
    # file-magic heuristic (which breaks on relocated/whitespace tool roots).
    lt_cv_deplibs_check_method=pass_all CPPFLAGS="-I$prefix/include" \
        LDFLAGS="$LDFLAGS -L$prefix/lib" LIBS='-liphlpapi -lws2_32' \
        ./configure --host=x86_64-w64-mingw32 --prefix="$prefix" "${event_mode[@]}" \
        --disable-openssl --disable-samples --disable-libevent-regress
    make -j"$jobs"
    make install
)
tor_features=(--disable-module-relay --disable-module-dirauth)
if [[ $profile == reference ]]; then tor_features=(); fi
(
    cd "$run/src/tor-0.4.9.14"
    ./configure --host=x86_64-w64-mingw32 --prefix="$prefix" \
        --with-libevent-dir="$prefix" --with-openssl-dir="$prefix" --with-zlib-dir="$prefix" \
        "${tor_link[@]}" "${tor_features[@]}" --disable-unittests --disable-asciidoc \
        --disable-systemd --disable-seccomp --disable-lzma --disable-zstd --disable-libscrypt
    if [[ $profile == optimized ]]; then
        if grep -Eq '^#define HAVE_MODULE_(RELAY|DIRAUTH|DIRCACHE) 1' orconfig.h; then
            die 'Client-only gate failed: relay/authority/cache module is enabled.'
        fi
    fi
    # Build only the actual daemon, retaining the upstream Makefile and rules.
    make -j"$jobs" src/app/tor.exe
    cp config.log orconfig.h Makefile "$run/"
)
out=$root/dist
[[ $profile == optimized && $linkage == static ]] || out=$root/dist-$profile-$linkage
bash "$project/scripts/package.sh" "$project" "$run" "$out"
printf 'PASS build and PE/package checks. Package: %s\n' "$out"
printf 'Run: %s\\start-tor.cmd\n' "$(cygpath -w "$out")"
if [[ -f $root/scripts/smoke-test.ps1 ]]; then
    printf 'Optional smoke: powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%s/scripts/smoke-test.ps1" -Dist "%s"\n' "$(cygpath -w "$root")" "$(cygpath -w "$out")"
fi
}
main "$@"; exit "$?"
