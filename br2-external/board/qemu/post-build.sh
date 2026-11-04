#!/bin/sh
set -eu
target=$1
project=$(CDPATH='' cd -- "$BR2_EXTERNAL_EMBEDDED_TOR_PATH/.." && pwd)
install -d "$target/opt/tor-validation/tests"
install -m 0644 "$project/tests/run_all.py" "$project/tests/echo_server.py" \
    "$target/opt/tor-validation/tests/"
install -m 0644 "$BR2_EXTERNAL_EMBEDDED_TOR_PATH/board/qemu/torrc.in" \
    "$target/opt/tor-validation/torrc.in"
chmod 0755 "$target/etc/init.d/S99tor-validation"
