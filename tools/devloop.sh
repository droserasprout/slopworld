#!/usr/bin/env bash
set -u

make_cmd=${MAKE_CMD:?MAKE_CMD is required}
repository=$(git rev-parse --path-format=absolute --git-common-dir) || exit 1
selected=$PWD
exec 3<>/dev/tty || { echo 'devloop requires an interactive terminal' >&2; exit 1; }
trap 'exit 130' INT
trap 'exit 143' TERM

while true; do
	paths=()
	labels=()
	while IFS= read -r -d '' field; do
		case "$field" in
			'worktree '*) paths+=("${field#worktree }"); labels+=("detached") ;;
			'branch '*) labels[${#labels[@]}-1]=${field#branch refs/heads/} ;;
		esac
	done < <(git --git-dir="$repository" worktree list --porcelain -z)
	if (( ${#paths[@]} == 0 )); then
		echo 'No worktrees available.' >&2
		exit 1
	fi
	for i in "${!paths[@]}"; do
		printf '%d) %s  %s\n' "$((i + 1))" "${labels[i]}" "${paths[i]}" >&3
	done
	printf 'Enter: %s; number: switch; q: quit > ' "$selected" >&3
	IFS= read -r choice <&3 || exit 0
	case "$choice" in
		q) exit 0 ;;
		'') ;;
		*)
			if [[ "$choice" =~ ^[1-9][0-9]*$ ]] && (( ${#choice} < 6 && choice <= ${#paths[@]} )); then
				selected=${paths[choice-1]}
			else
				echo 'Choose a listed number.' >&3
				continue
			fi ;;
	esac
	if [[ ! -d "$selected" ]]; then
		printf 'Checkout unavailable: %s\n' "$selected" >&3
		continue
	fi
	printf '\nBuilding %s (%s)\n' "$selected" "$(git -C "$selected" rev-parse --short HEAD)" >&3
	"$make_cmd" -C "$selected" install && "$make_cmd" -C "$selected" run
done
