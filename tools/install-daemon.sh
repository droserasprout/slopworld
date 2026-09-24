#!/usr/bin/env bash
set -euo pipefail

target=${TARGET:?TARGET is required}
bin=${BIN:?BIN is required}
units=${UNITS:?UNITS is required}
build=${BUILD:?BUILD is required}
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)

# Materialize explicit diagnostic overrides in the service itself: a user service
# does not inherit the installing shell's environment. Compare this effective unit
# so changing only a tracing flag also restarts an otherwise identical daemon.
service_source=$(mktemp)
trap 'rm -f "$service_source"' EXIT
cat "$repo/slopd/slopd.service" > "$service_source"
for flag in SLOPWORLD_DEBUG; do
	if [[ -v "$flag" ]]; then
		value=${!flag}
		case "${value,,}" in
			0|1|true|false|'') ;;
			*) printf '%s must be 0, 1, true, false, or empty\n' "$flag" >&2; exit 1 ;;
		esac
		printf '\n[Service]\nEnvironment=%s=%s\n' "$flag" "$value" >> "$service_source"
	fi
done

restart=yes
if systemctl --user is-active --quiet slopd.service; then
	pid=$(systemctl --user show --property=MainPID --value slopd.service)
	if test "$pid" -gt 0 2>/dev/null && cmp -s "$target/slopd" "/proc/$pid/exe" \
		&& cmp -s "$service_source" "$units/slopd.service"; then
		restart=no
		echo "slopd already runs the latest $build build and service unit. The daemon does not need a restart."
	fi
fi

install -Dm755 "$target/slopd" "$bin/slopd"
install -Dm755 "$target/slopctl" "$bin/slopctl"
install -Dm644 "$service_source" "$units/slopd.service"
systemctl --user daemon-reload
systemctl --user enable --now slopd.service
if test "$restart" = yes; then
	systemctl --user restart slopd.service
fi
systemctl --user --no-pager status slopd.service | head -3 || true
