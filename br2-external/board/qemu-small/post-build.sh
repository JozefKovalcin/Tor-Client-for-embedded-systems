#!/bin/sh
set -eu
target=$1
[ "$target" = "${TARGET_DIR:?Buildroot TARGET_DIR is required}" ] || exit 2
"$BR2_EXTERNAL_EMBEDDED_TOR_PATH/board/qemu/post-build.sh" "$target"
# Optional country lookup/statistics and offline authority/diagnostic tools.
# No country filters or relay statistics are enabled in this client profile.
# Tor's cryptographic libraries and all protocol implementation stay intact.
rm -f -- "$target/usr/share/tor/geoip" "$target/usr/share/tor/geoip6" \
    "$target/usr/bin/tor-gencert" "$target/usr/bin/tor-print-ed-signing-cert" \
    "$target/usr/bin/tor-resolve"
