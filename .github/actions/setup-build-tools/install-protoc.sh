#!/usr/bin/env bash
# Pinned Linux x86_64 protoc for generated binding checks.
set -euo pipefail

version=36.1
checksum=c4bc672d9d49214dc8cafdceadf4df92182d6ca8e3ec65a56b2d7de5602669b4
archives="$RUNNER_TEMP/slopworld-protoc"
destination="$RUNNER_TEMP/slopworld-build-bin/protoc"
archive="$archives/protoc-$version.zip"
mkdir -p "$archives" "$destination"

if [[ ! -f "$archive" ]]; then
    curl --retry 3 -fsSL \
        "https://github.com/protocolbuffers/protobuf/releases/download/v$version/protoc-$version-linux-x86_64.zip" \
        -o "$archive.part"
    mv "$archive.part" "$archive"
fi
printf '%s  %s\n' "$checksum" "$archive" | sha256sum --check
unzip -oq "$archive" -d "$destination"
echo "$destination/bin" >> "$GITHUB_PATH"
