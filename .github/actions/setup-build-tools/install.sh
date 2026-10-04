#!/usr/bin/env bash
# Shared tool pins for test and release builds.
set -euo pipefail

just_version=1.58.0
protoc_version=36.1
protoc_checksum=c4bc672d9d49214dc8cafdceadf4df92182d6ca8e3ec65a56b2d7de5602669b4
uv_version=0.12.23
uv_checksum=9167d72b3319674b6303c4cbe071854bba13ebdf3d76b1a7cbdc175471fb66d6
llvm_cov_version=0.9.1
archives="$RUNNER_TEMP/slopworld-build-tools"
destination="$RUNNER_TEMP/slopworld-build-bin"
mkdir -p "$archives" "$destination"

download_archive() {
    local name="$1" url="$2"
    local archive="$archives/$name"
    if test -f "$archive"; then
        return
    fi
    # Publish only complete downloads so a failed fetch cannot poison the cache.
    curl --retry 3 -fsSL "$url" -o "$archive.part"
    mv "$archive.part" "$archive"
}

download_archive "just-$just_version.tar.gz" \
    "https://github.com/casey/just/releases/download/$just_version/just-$just_version-x86_64-unknown-linux-musl.tar.gz"
tar -xzf "$archives/just-$just_version.tar.gz" -C "$destination" just

download_archive "protoc-$protoc_version.zip" \
    "https://github.com/protocolbuffers/protobuf/releases/download/v$protoc_version/protoc-$protoc_version-linux-x86_64.zip"
printf '%s  %s\n' "$protoc_checksum" "$archives/protoc-$protoc_version.zip" | sha256sum --check
unzip -oq "$archives/protoc-$protoc_version.zip" -d "$destination/protoc"

download_archive "uv-$uv_version.tar.gz" \
    "https://github.com/astral-sh/uv/releases/download/$uv_version/uv-x86_64-unknown-linux-gnu.tar.gz"
printf '%s  %s\n' "$uv_checksum" "$archives/uv-$uv_version.tar.gz" | sha256sum --check
tar -xzf "$archives/uv-$uv_version.tar.gz" -C "$destination" --strip-components=1 uv-x86_64-unknown-linux-gnu/uv

if [[ "${INSTALL_COVERAGE:-false}" == true ]]; then
    download_archive "cargo-llvm-cov-$llvm_cov_version.tar.gz" \
        "https://github.com/taiki-e/cargo-llvm-cov/releases/download/v$llvm_cov_version/cargo-llvm-cov-x86_64-unknown-linux-musl.tar.gz"
    tar -xzf "$archives/cargo-llvm-cov-$llvm_cov_version.tar.gz" -C "$destination" cargo-llvm-cov
fi

echo "$destination" >> "$GITHUB_PATH"
echo "$destination/protoc/bin" >> "$GITHUB_PATH"
