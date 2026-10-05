# Installation

# Install the binary and the unit, restarting only when needed
[group('Installation')]
install-daemon: daemon
    TARGET="{{ TARGET }}" BIN="{{ BIN }}" UNITS="{{ UNITS }}" BUILD="{{ BUILD }}" \
            {{ PYTHON }} -m tools.host.install_daemon

# Install the launcher beside the daemon
[group('Installation')]
install-runner: daemon
    install -Dm755 "{{ RUNNER }}" "{{ BIN }}/slopworld"
    echo "installed to {{ BIN }}/slopworld"

# Install the mod into the game's Mods folder
[group('Installation')]
install-mod: daemon mod
    "{{ RUNNER }}" mod install --source mod --mods "{{ MODS }}"

# Remove installed files, keeping config and saves
[group('Installation')]
uninstall: uninstall-daemon uninstall-runner uninstall-mod uninstall-font
    echo "left alone: ~/.config/slopworld, the profile (saves), any tmux server"
    echo "under the slopworld socket; \`tmux -L slopworld kill-server\` ends the agents."

# Stop the service, remove the binary and the unit
[group('Installation')]
uninstall-daemon:
    -systemctl --user disable --now slopd.service
    rm -f "{{ UNITS }}/slopd.service"
    rm -f "{{ BIN }}/slopd"
    rm -f "{{ BIN }}/slopctl"
    systemctl --user daemon-reload
    echo "removed {{ BIN }}/slopd, {{ BIN }}/slopctl and {{ UNITS }}/slopd.service"

# Remove the launcher
[group('Installation')]
uninstall-runner:
    rm -f "{{ BIN }}/slopworld"
    echo "removed {{ BIN }}/slopworld"

# Remove the installed mod folder
[group('Installation')]
uninstall-mod: daemon
    "{{ RUNNER }}" mod uninstall --mods "{{ MODS }}"

# Remove a legacy per-user copy of the loading font
[group('Installation')]
uninstall-font:
    rm -f "{{ FONT_DEST }}"
    if command -v fc-cache >/dev/null 2>&1; then fc-cache -f "{{ FONT_DIR }}"; fi
    echo "removed font {{ FONT_DEST }}"
