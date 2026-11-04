#!/bin/sh
set -eu
target=$1
[ "$target" = "${TARGET_DIR:?}" ] || exit 2
"$BR2_EXTERNAL_EMBEDDED_TOR_PATH/board/qemu-production/post-build.sh" "$target"
# Mountpoints must exist on the immutable root, including the lab-only fixture.
mkdir -p "$target/var/lib/tor" "$target/mnt/validation"
# Tor links the monolithic libevent; these unused split-library variants and
# operator/development helpers are not part of this dedicated runtime.
rm -f -- "$target"/usr/lib/libevent_core* "$target"/usr/lib/libevent_extra* \
    "$target"/usr/lib/libevent_openssl* "$target"/usr/lib/libevent_pthreads* \
    "$target/usr/bin/scmp_sys_resolver" "$target/usr/bin/torify" \
    "$target/etc/tor/torrc.sample" "$target/etc/ssl/openssl.cnf.dist"
