#!/bin/sh
set -eu

socket=slopcar-doctor-$$
cleanup() {
    tmux -L "$socket" kill-server >/dev/null 2>&1 || true
}
trap cleanup EXIT HUP INT TERM

# Exercise NSS and OpenSSH initialization without connecting to a remote host.
python3 - <<'PY'
import grp
import os
import pwd
from pathlib import Path

assert os.geteuid() != 0
p = pwd.getpwuid(os.geteuid())
assert p.pw_name == "slop" and p.pw_gid == os.getegid() and p.pw_dir == "/home/slop"
assert grp.getgrnam("slop").gr_gid == os.getegid()
grp.getgrgid(os.getegid())
assert os.getgroups() == []
for line in Path('/proc/self/status').read_text().splitlines():
    if line.startswith(('CapInh:', 'CapPrm:', 'CapEff:', 'CapBnd:', 'CapAmb:')):
        assert int(line.split()[1], 16) == 0, line
PY
ssh -G -F /dev/null localhost >/dev/null

resolver=$(awk '$1 == "nameserver" && $2 ~ /^[0-9.]+$/ { print $2; exit }' /etc/resolv.conf)
if [ -z "$resolver" ]; then
    echo "slopcar doctor: no IPv4 resolver in /etc/resolv.conf" >&2
    exit 1
fi

# Recreate the host's top-level library and binary entries in the sandbox skeleton.
# Read the entries from the host because Linux distributions use different library layouts.
# For example, Arch uses /usr/lib. Debian can use /lib64 on amd64 but not on arm64.
set --
for link in /lib /lib64 /bin /sbin; do
    if [ -L "$link" ]; then
        set -- "$@" --symlink "$(readlink "$link")" "$link"
    elif [ -d "$link" ]; then
        set -- "$@" --ro-bind "$link" "$link"
    fi
done

pasta \
    --foreground \
    --quiet \
    --config-net \
    --no-map-gw \
    --ipv4-only \
    --tcp-ports none \
    --udp-ports none \
    --tcp-ns none \
    --udp-ns none \
    --address 192.0.2.2 \
    --netmask 24 \
    --gateway 192.0.2.1 \
    --dns-forward 192.0.2.1 \
    --dns-host "$resolver" \
    -- \
    bwrap \
        --die-with-parent \
        --unshare-all \
        --uid "$(id -u)" \
        --gid "$(id -g)" \
        --share-net \
        --clearenv \
        "$@" \
        --ro-bind /usr /usr \
        --ro-bind /etc /etc \
        --proc /proc \
        --dev /dev \
        --tmpfs /tmp \
        -- /bin/sh -ec '
            test "$(id -u)" = "$1"
            test "$(id -g)" = "$2"
            /usr/bin/ssh -G -F /dev/null localhost
        ' sh "$(id -u)" "$(id -g)" >/dev/null

tmux -L "$socket" new-session -d -s doctor -- /usr/bin/sleep 5
tmux -L "$socket" has-session -t doctor

echo "slopcar doctor: accounts + SSH + bwrap + pasta + tmux passed. Resolver: $resolver"
