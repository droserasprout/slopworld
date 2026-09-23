#!/usr/bin/env bash
# Called by Make from the repository root. Settings come from make/config.mk.
set -euo pipefail

case "${1:-}" in
login)
mkdir -p "$(dirname "$GOGDL_AUTH")"
if command -v xdg-open >/dev/null 2>&1; then
	xdg-open "${GOGDL_LOGIN_URL}" >/dev/null 2>&1 || true
fi
echo "Log into GOG in your browser, then paste the authorization code here."
echo "If the browser did not open, visit: ${GOGDL_LOGIN_URL}"
printf "Authorization code: " >&2
read -r code
test -n "$code" || { echo "authorization code is empty" >&2; exit 1; }
${GOGDL} --auth-config-path "${GOGDL_AUTH}" auth --code "$code"
test -s "${GOGDL_AUTH}" || { echo "gogdl did not save credentials to ${GOGDL_AUTH}" >&2; exit 1; }
;;
install)
test -s "${GOGDL_AUTH}" || { echo "The gogdl login is missing at ${GOGDL_AUTH}. Run make gogdl-login." >&2; exit 1; }
mkdir -p "${GOGDL_PATH}" "$(dirname "$GOGDL_AUTH")"
${GOGDL} --auth-config-path "${GOGDL_AUTH}" download "${GOGDL_ID}" \
	--path "${GOGDL_PATH}" --platform linux --with-dlcs
;;
update)
test -x "${RIMWORLD}/RimWorldLinux" || { echo "RimWorld is missing at ${RIMWORLD}. Run make gogdl-install or set RIMWORLD." >&2; exit 1; }
test -s "${GOGDL_AUTH}" || { echo "The gogdl login is missing at ${GOGDL_AUTH}. Run make gogdl-login." >&2; exit 1; }
mkdir -p "$(dirname "$GOGDL_AUTH")"
${GOGDL} --auth-config-path "${GOGDL_AUTH}" update "${GOGDL_ID}" \
	--path "${RIMWORLD}" --platform linux --with-dlcs
;;
*) echo "usage: $0 {login|install|update}" >&2; exit 2 ;;
esac
