#!/usr/bin/env bash
set -euo pipefail

just_cmd=${JUST_CMD:?JUST_CMD is required}
"$just_cmd" mac-install
"$just_cmd" mac-run
