#!/usr/bin/env bash
set -euo pipefail

input=${1:-/home/droserasprout/git/slopworld/mod/Textures/SlopWorld/SlopWorld_icon.png}
output=${2:-/tmp/SlopWorld_icon_zoomed.png}
zoom=${3:-1.5}

if command -v magick >/dev/null 2>&1; then
  image_tool=magick
elif command -v convert >/dev/null 2>&1; then
  image_tool=convert
else
  echo "ImageMagick (magick or convert) is required" >&2
  exit 1
fi

if [[ ! -f "$input" ]]; then
  echo "Input image not found: $input" >&2
  exit 1
fi

# Trim only the transparent border, scale the artwork around its center, then
# place it back on the original canvas. The fixed canvas keeps desktop-icon
# dimensions and the alpha channel intact.
trimmed=$(mktemp --suffix=.png)
scaled=$(mktemp --suffix=.png)
trap 'rm -f "$trimmed" "$scaled"' EXIT

"$image_tool" "$input" -trim +repage "$trimmed"
resize_percent=$(awk "BEGIN { printf \"%.4g%%\", ${zoom} * 100 }")
"$image_tool" "$trimmed" -alpha on -filter Lanczos -resize "$resize_percent" "$scaled"
"$image_tool" -size 128x128 xc:none \
  "$scaled" -gravity center -composite \
  -define png:color-type=6 "$output"

echo "Wrote $output"
