#!/usr/bin/env bash
set -euo pipefail

make_cmd=${MAKE_CMD:?MAKE_CMD is required}
"$make_cmd" mac-install
"$make_cmd" mac-run
