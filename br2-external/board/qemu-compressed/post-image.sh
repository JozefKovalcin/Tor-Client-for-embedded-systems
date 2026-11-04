#!/bin/sh
set -eu
# Every invocation gets a fresh temporary workspace; preserve it for diagnosis.
work=$(mktemp -d "${BUILD_DIR:?}/tor-genimage.XXXXXX")
mkdir "$work/root"
exec "$HOST_DIR/bin/genimage" --rootpath "$work/root" --tmppath "$work/work" \
    --inputpath "$BINARIES_DIR" --outputpath "$BINARIES_DIR" \
    --config "$BR2_EXTERNAL_EMBEDDED_TOR_PATH/board/qemu-compressed/genimage.cfg"
