.PHONY: install-daemon install-runner install-mod install-font \
	uninstall uninstall-daemon uninstall-runner uninstall-mod uninstall-font

##

install-daemon: daemon ## Install the binary and the unit, restarting only when needed
	TARGET="$(TARGET)" BIN="$(BIN)" UNITS="$(UNITS)" BUILD="$(BUILD)" \
		tools/install-daemon.sh

install-runner: daemon ## Install the launcher beside the daemon
	install -Dm755 "$(RUNNER)" "$(BIN)/slopworld"
	@echo "installed to $(BIN)/slopworld"

install-mod: mod       ## Install the mod into the game's Mods folder
	"$(RUNNER)" mod install --source mod --mods "$(MODS)"

install-font:           ## Install the bundled UI font into the current user's font directory
	@test -f "$(FONT_SOURCE)" || { echo "missing bundled font: $(FONT_SOURCE)" >&2; exit 1; }
	install -Dm644 "$(FONT_SOURCE)" "$(FONT_DEST)"
	@if command -v fc-cache >/dev/null 2>&1; then fc-cache -f "$(FONT_DIR)"; fi
	@echo "installed font to $(FONT_DEST)"

##

uninstall: uninstall-daemon uninstall-runner uninstall-mod uninstall-font ## Remove installed files, keeping config and saves
	@echo "left alone: ~/.config/slopworld, the profile (saves), any tmux server"
	@echo "under the slopworld socket; \`tmux -L slopworld kill-server\` ends the agents."

uninstall-daemon:  ## Stop the service, remove the binary and the unit
	-systemctl --user disable --now slopd.service
	rm -f "$(UNITS)/slopd.service"
	rm -f "$(BIN)/slopd"
	rm -f "$(BIN)/slopctl"
	systemctl --user daemon-reload
	@echo "removed $(BIN)/slopd, $(BIN)/slopctl and $(UNITS)/slopd.service"

uninstall-runner:  ## Remove the launcher
	rm -f "$(BIN)/slopworld"
	@echo "removed $(BIN)/slopworld"

uninstall-mod: daemon ## Remove the installed mod folder
	"$(RUNNER)" mod uninstall --mods "$(MODS)"

uninstall-font:        ## Remove the bundled UI font from the current user's font directory
	rm -f "$(FONT_DEST)"
	@if command -v fc-cache >/dev/null 2>&1; then fc-cache -f "$(FONT_DIR)"; fi
	@echo "removed font $(FONT_DEST)"
