#!/usr/bin/env bash
set -euo pipefail

just_cmd=${JUST_CMD:?JUST_CMD is required}
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
"$just_cmd" --justfile "$repo/mac/justfile" install
"$just_cmd" --justfile "$repo/mac/justfile" run
