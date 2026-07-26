RIMWORLD   ?= $(HOME)/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
API        ?= http://127.0.0.1:7717
TOKEN      ?=

.PHONY: all daemon mod install install-daemon install-mod redeploy run logs test clean

all: daemon mod

daemon:
	cd slopd && cargo build --release

mod:
	cd mod/Source/SlopWorld && msbuild -restore -v:minimal -p:Configuration=Release \
		-p:RimWorldManaged="$(MANAGED)" SlopWorld.csproj

test:
	cd slopd && cargo test

install: install-daemon install-mod

install-daemon: daemon
	install -Dm755 slopd/target/release/slopd $(BIN)/slopd
	install -Dm644 slopd/slopd.service $(UNITS)/slopd.service
	systemctl --user daemon-reload
	systemctl --user enable --now slopd.service
	systemctl --user restart slopd.service
	@systemctl --user --no-pager status slopd.service | head -3

# The install is wiped rather than copied over. cp -r never deletes, so a def or
# a texture dropped from the repo stayed installed and kept working - which is
# worse than a build error, because the game loads the stale def and the symptom
# is the old behaviour with none of the old code behind it. The guard is because
# this rm is only ever safe on a path we built ourselves.
install-mod: mod
	@test -n "$(MODS)" || { echo "MODS is empty, refusing to remove anything"; exit 1; }
	rm -rf "$(MODS)/SlopWorld"
	mkdir -p "$(MODS)/SlopWorld"
	cp -r mod/About mod/Defs mod/Patches mod/Sounds mod/Textures mod/Assemblies "$(MODS)/SlopWorld/"
	@echo "installed to $(MODS)/SlopWorld"

# The whole loop from inside a session: install both halves, then ask the daemon
# to bounce the game so the new mod is loaded. The game saves on its way out and
# comes back into the same colony, so an agent working on the mod can see its own
# change without anyone touching the keyboard. Needs daemon.game_cmd set in
# config.toml; without it the call 400s and only the install has happened.
redeploy: install
	@curl -fsS -X POST "$(API)/api/game/restart" \
		-H "x-slop-token: $(TOKEN)" -H "content-type: application/json" \
		-d '{"delay_ms":4000}' >/dev/null \
		&& echo "game restart requested" \
		|| echo "game not restarted (is daemon.game_cmd set, and the game running?)"

run:
	"$(RIMWORLD)/RimWorldLinux" -popupwindow -force-opengl

# The game's own log; Harmony and mod errors land here, not in the terminal.
logs:
	@tail -f "$(LOG)"

clean:
	cd slopd && cargo clean
	rm -f mod/Assemblies/SlopWorld.dll
	rm -rf mod/Source/SlopWorld/obj
