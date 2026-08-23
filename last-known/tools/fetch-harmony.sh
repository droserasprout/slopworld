#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "$0")/.." && pwd)
destination=${1:-"$repo_root/mod/Assemblies"}
api_url=https://api.github.com/repos/pardeike/HarmonyRimWorld/releases/latest
asset_name=HarmonyMod.zip
archive_entry=HarmonyMod/Current/Assemblies/0Harmony.dll

for command in curl python3 sha256sum unzip install mv mktemp; do
	if ! command -v "$command" >/dev/null 2>&1; then
		echo "error: required command not found: $command" >&2
		exit 1
	fi
done

tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/slopworld-harmony.XXXXXX")
trap 'rm -rf -- "$tmp_dir"' EXIT

release_json="$tmp_dir/release.json"
archive="$tmp_dir/$asset_name"

curl --fail --silent --show-error --location \
	-H 'Accept: application/vnd.github+json' \
	-H 'X-GitHub-Api-Version: 2022-11-28' \
	-o "$release_json" "$api_url"

metadata=$(python3 - "$release_json" "$asset_name" <<'PY'
import json
import sys

release_path, asset_name = sys.argv[1:]
with open(release_path, encoding="utf-8") as stream:
    release = json.load(stream)

asset = next((item for item in release.get("assets", [])
              if item.get("name") == asset_name), None)
if asset is None:
    raise SystemExit(f"latest release has no {asset_name} asset")

digest = asset.get("digest", "")
if not digest.startswith("sha256:"):
    raise SystemExit(f"{asset_name} has no SHA-256 digest")

print("\t".join((release["tag_name"], asset["browser_download_url"], digest[7:])))
PY
)
IFS=$'\t' read -r version download_url expected_digest <<<"$metadata"

printf 'Fetching Harmony %s...\n' "$version"
curl --fail --silent --show-error --location \
	-o "$archive" "$download_url"

actual_digest=$(sha256sum "$archive" | awk '{print $1}')
if [[ "$actual_digest" != "$expected_digest" ]]; then
	echo "error: downloaded asset failed SHA-256 verification" >&2
	echo "expected: $expected_digest" >&2
	echo "actual:   $actual_digest" >&2
	exit 1
fi

unpacked="$tmp_dir/unpacked"
mkdir -p -- "$unpacked"
unzip -q "$archive" "$archive_entry" -d "$unpacked"
extracted="$unpacked/$archive_entry"
if [[ ! -s "$extracted" ]]; then
	echo "error: archive did not contain a non-empty $archive_entry" >&2
	exit 1
fi

mkdir -p -- "$destination"
staged="$destination/.0Harmony.dll.$$"
install -m 0644 "$extracted" "$staged"
mv -f -- "$staged" "$destination/0Harmony.dll"

printf 'Installed Harmony %s at %s\n' "$version" "$destination/0Harmony.dll"
