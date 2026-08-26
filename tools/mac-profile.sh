#!/usr/bin/env bash
set -euo pipefail

profile=${MAC_PROFILE:?MAC_PROFILE is required}
case "$profile" in
	*=*)
		echo "MAC_PROFILE cannot contain '=': $profile" >&2
		exit 1
		;;
esac

mkdir -p "$profile/Config"

if [[ ! -e "$profile/slopworld.profile" ]]; then
	printf '%s\n' \
		'This is a SlopWorld profile.' \
		'The marker keeps the mod out of ordinary RimWorld saves.' \
		> "$profile/slopworld.profile"
fi

if [[ ! -e "$profile/Config/SlopWorld.toml" ]]; then
	printf '%s\n' \
		'uiScheme = "slopworld-warm"' \
		> "$profile/Config/SlopWorld.toml"
fi

if [[ ! -e "$profile/Config/ModsConfig.xml" ]]; then
	cat > "$profile/Config/ModsConfig.xml" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <activeMods>
    <li>ludeon.rimworld</li>
    <li>drsr.slopworld</li>
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
EOF
fi

echo "macOS sidecar profile: $profile"
