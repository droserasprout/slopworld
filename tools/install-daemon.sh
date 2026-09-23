#!/usr/bin/env bash
set -euo pipefail

target=${TARGET:?TARGET is required}
bin=${BIN:?BIN is required}
units=${UNITS:?UNITS is required}
build=${BUILD:?BUILD is required}
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)

restart=yes
if systemctl --user is-active --quiet slopd.service; then
	pid=$(systemctl --user show --property=MainPID --value slopd.service)
	if test "$pid" -gt 0 2>/dev/null && cmp -s "$target/slopd" "/proc/$pid/exe" \
		&& cmp -s "$repo/slopd/slopd.service" "$units/slopd.service"; then
		restart=no
		echo "slopd already runs the latest $build build and service unit. The daemon does not need a restart."
	fi
fi

install -Dm755 "$target/slopd" "$bin/slopd"
install -Dm755 "$target/slopctl" "$bin/slopctl"
install -Dm644 "$repo/slopd/slopd.service" "$units/slopd.service"
systemctl --user daemon-reload
systemctl --user enable --now slopd.service
if test "$restart" = yes; then
	systemctl --user restart slopd.service
fi
systemctl --user --no-pager status slopd.service | head -3 || true
