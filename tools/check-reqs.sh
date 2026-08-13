#!/bin/sh
# Reports the host commands and files used by the build and by the default UI.
# Required failures make the target fail; optional integrations and devtools are
# informational.

required_missing=0

have() {
	command -v "$1" >/dev/null 2>&1
}

required_command() {
	label=$1
	command=$2
	if have "$command"; then
		printf '  ✅ %-46s %s\n' "$label" "$command"
	else
		printf '  ❌ %-46s missing: %s\n' "$label" "$command"
		required_missing=1
	fi
}

required_any() {
	label=$1
	shift
	found=
	for command in "$@"; do
		if have "$command"; then
			found=$command
			break
		fi
	done
	if [ -n "$found" ]; then
		printf '  ✅ %-46s %s\n' "$label" "$found"
	else
		printf '  ❌ %-46s missing one of: %s\n' "$label" "$*"
		required_missing=1
	fi
}

optional_command() {
	label=$1
	command=$2
	if have "$command"; then
		printf '  ✨ %-46s %s\n' "$label" "$command"
	else
		printf '  ◽ %-46s not detected: %s\n' "$label" "$command"
	fi
}

optional_any() {
	label=$1
	shift
	found=
	for command in "$@"; do
		if have "$command"; then
			found=$command
			break
		fi
	done
	if [ -n "$found" ]; then
		printf '  ✨ %-46s %s\n' "$label" "$found"
	else
		printf '  ◽ %-46s not detected: %s\n' "$label" "$*"
	fi
}

optional_all() {
	label=$1
	shift
	missing=
	for command in "$@"; do
		if ! have "$command"; then
			missing="$missing $command"
		fi
	done
	if [ -z "$missing" ]; then
		printf '  ✨ %-46s %s\n' "$label" "$*"
	else
		printf '  ◽ %-46s missing:%s\n' "$label" "$missing"
	fi
}

required_library() {
	label=$1
	needle=$2
	if [ -e "/usr/lib/$needle" ] || [ -e "/usr/lib64/$needle" ] ||
		(have ldconfig && ldconfig -p 2>/dev/null | grep -q "$needle"); then
		printf '  ✅ %-46s %s\n' "$label" "$needle"
	else
		printf '  ❌ %-46s missing: %s\n' "$label" "$needle"
		required_missing=1
	fi
}

required_game() {
	game=${RIMWORLD:-}
	managed="$game/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"
	executable="$game/RimWorldLinux"
	if [ -f "$managed" ] && [ -f "$executable" ]; then
		printf '  ✅ %-46s %s\n' 'native RimWorld install' "$game"
	else
		printf '  ❌ %-46s missing or incomplete: %s\n' 'native RimWorld install' "${game:-RIMWORLD is unset}"
		required_missing=1
	fi
}

optional_python_modules() {
	if ! have python3; then
		printf '  ◽ %-46s not detected: python3\n' 'Python image modules'
		printf '  ◽ %-46s not detected: python3\n' 'Python Pango modules'
		return
	fi
	if python3 -c 'import numpy, PIL' >/dev/null 2>&1; then
		printf '  ✨ %-46s numpy + Pillow\n' 'Python image modules'
	else
		printf '  ◽ %-46s missing: numpy and/or Pillow\n' 'Python image modules'
	fi
	if python3 -c 'import cairo, gi' >/dev/null 2>&1; then
		printf '  ✨ %-46s Pycairo + PyGObject\n' 'Python Pango modules'
	else
		printf '  ◽ %-46s missing: Pycairo and/or PyGObject\n' 'Python Pango modules'
	fi
}

optional_fonts() {
	if have fc-match && fc-match -f '%{family}\n' 'Noto Color Emoji' 2>/dev/null |
		grep -qi 'Noto Color Emoji'; then
		printf '  ✨ %-46s Noto Color Emoji\n' 'Emoji font'
	else
		printf '  ◽ %-46s not detected: Noto Color Emoji\n' 'Emoji font'
	fi
	if have fc-list && fc-list 2>/dev/null | grep -qi 'Nerd Font'; then
		printf '  ✨ %-46s Nerd Font\n' 'Codicon bake font'
	else
		printf '  ◽ %-46s not detected: Nerd Font\n' 'Codicon bake font'
	fi
}

printf '%s\n' 'SlopWorld requirements'
printf '%s\n' 'Required: build toolchain and default runtime'
required_command 'Rust/Cargo' cargo
required_command 'C# builder' msbuild
required_game
required_command 'session multiplexer' tmux
required_command 'sandbox isolation' bwrap
required_command 'private networking' pasta
required_command 'user service control' systemctl
required_command 'user scopes' systemd-run
required_command 'game process lookup' pgrep
required_command 'workspace search' rg
required_command 'Git view' git
required_command 'default shell' bash
required_command 'default pager' less
required_command 'pager syntax highlighting' highlight
required_command 'default editor' micro
required_library 'daemon audio backend' libasound.so.2
required_any 'agent CLI (one of)' claude codex opencode pi

printf '\n%s\n' 'Optional: integrations'
optional_all 'Wayland clipboard' wl-copy wl-paste
optional_any 'X11 clipboard' xclip xsel
optional_any 'URL opener' xdg-open gio wslview

printf '\n%s\n' 'Optional: developer tools'
optional_command 'redeploy helper' curl
optional_command 'C# formatting/linting' dotnet
optional_command 'human docs' mdbook
optional_all 'game screenshot' xdotool import
optional_command 'OST audio conversion' ffmpeg
optional_command 'Python tooling' python3
optional_python_modules
optional_any 'SVG rasterizer' rsvg-convert
if have python3 && python3 -c 'import cairosvg' >/dev/null 2>&1; then
	printf '  ✨ %-46s CairoSVG\n' 'SVG rasterizer fallback'
else
	printf '  ◽ %-46s not detected: CairoSVG\n' 'SVG rasterizer fallback'
fi
optional_fonts

if [ "$required_missing" -ne 0 ]; then
	printf '\n❌ One or more required requirements are missing.\n'
	exit 1
fi
printf '\n✅ Required requirements detected.\n'
