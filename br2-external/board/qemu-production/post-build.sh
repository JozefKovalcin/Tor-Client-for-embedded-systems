#!/bin/sh
set -eu
target=$1
[ "$target" = "${TARGET_DIR:?Buildroot TARGET_DIR is required}" ] || exit 2
# These resources serve unused country filters/statistics or offline tools.
rm -f -- "$target/usr/share/tor/geoip" "$target/usr/share/tor/geoip6" \
    "$target/usr/bin/tor-gencert" "$target/usr/bin/tor-print-ed-signing-cert" \
    "$target/usr/bin/tor-resolve"
chmod 0755 "$target/etc/init.d/S50tor"
