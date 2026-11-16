#!/usr/bin/env bash
set -Eeuo pipefail
[[ $# == 3 ]] || { echo 'usage: package.sh PROJECT BUILD_RUN OUTPUT' >&2; exit 2; }
SOURCE_DATE_EPOCH=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["source_date_epoch"])' "$1/source-manifest.json")
export SOURCE_DATE_EPOCH TZ=UTC LC_ALL=C
python3 "$1/scripts/package.py" "$1" "$2" "$3" "${MINGW_PREFIX:-/ucrt64}/bin"
