RIMWORLD   ?= $(HOME)/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
API        ?= http://127.0.0.1:7717
TOKEN      ?=
# The save data folder the game is launched into; blank lets the runner pick its
# own default ($XDG_DATA_HOME/slopworld/profile).
PROFILE    ?=
RUNNER      = slopd/target/release/slopworld

.PHONY: all daemon mod install install-daemon install-runner install-mod redeploy run logs shot test clean

all: daemon mod

daemon:
	cd slopd && cargo build --release

mod:
	cd mod/Source/SlopWorld && msbuild -restore -v:minimal -p:Configuration=Release \
		-p:RimWorldManaged="$(MANAGED)" SlopWorld.csproj

test:
	cd slopd && cargo test

install: install-daemon install-runner install-mod

install-daemon: daemon
	install -Dm755 slopd/target/release/slopd $(BIN)/slopd
	install -Dm644 slopd/slopd.service $(UNITS)/slopd.service
	systemctl --user daemon-reload
	systemctl --user enable --now slopd.service
	systemctl --user restart slopd.service
	@systemctl --user --no-pager status slopd.service | head -3

# The launcher. Built by the same cargo invocation as the daemon, being a second
# binary in that crate, and installed beside it so `daemon.game_cmd = "slopworld"`
# resolves.
install-runner: daemon
	install -Dm755 $(RUNNER) $(BIN)/slopworld
	@echo "installed to $(BIN)/slopworld"

# Wiped rather than copied over: cp -r never deletes, so a def dropped from the
# repo stayed installed and the game went on loading it. The guard is because
# this rm is only ever safe on a path we built ourselves.
install-mod: mod
	@test -n "$(MODS)" || { echo "MODS is empty, refusing to remove anything"; exit 1; }
	rm -rf "$(MODS)/SlopWorld"
	mkdir -p "$(MODS)/SlopWorld"
	cp -r mod/About mod/Defs mod/Patches mod/Sounds mod/Textures mod/Assemblies "$(MODS)/SlopWorld/"
	@echo "installed to $(MODS)/SlopWorld"

# Install both halves, then ask the daemon to bounce the game: it saves on the
# way out and comes back into the same colony, so an agent working on the mod
# sees its own change. Needs daemon.game_cmd set, or the call 400s.
redeploy: install
	@curl -fsS -X POST "$(API)/api/game/restart" \
		-H "x-slop-token: $(TOKEN)" -H "content-type: application/json" \
		-d '{"delay_ms":4000}' >/dev/null \
		&& echo "game restart requested" \
		|| echo "game not restarted (is daemon.game_cmd set, and the game running?)"

# Through the runner, never at the binary: the game is launched into a save data
# folder of its own, and the mod refuses to patch anything outside one.
run: daemon
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)") \
		-popupwindow -force-opengl

# The game's own log; Harmony and mod errors land here, not in the terminal.
logs:
	@tail -f "$(LOG)"

# Needs the `x11` preset on this project's sandbox; see tools/shot.sh.
shot:
	@tools/shot.sh $(OUT)

clean:
	cd slopd && cargo clean
	rm -f mod/Assemblies/SlopWorld.dll
	rm -rf mod/Source/SlopWorld/obj
