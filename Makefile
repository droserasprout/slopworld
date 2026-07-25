RIMWORLD   ?= $(HOME)/RimWorld/game
MANAGED    ?= $(RIMWORLD)/RimWorldLinux_Data/Managed
MODS       ?= $(RIMWORLD)/Mods
BIN        ?= $(HOME)/.local/bin
UNITS      ?= $(HOME)/.config/systemd/user
LOG        ?= $(HOME)/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log

.PHONY: all daemon mod install install-daemon install-mod run logs test clean

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

install-mod: mod
	mkdir -p "$(MODS)/SlopWorld"
	cp -r mod/About mod/Defs mod/Patches mod/Sounds mod/Textures mod/Assemblies "$(MODS)/SlopWorld/"
	@echo "installed to $(MODS)/SlopWorld"

run:
	"$(RIMWORLD)/RimWorldLinux" -popupwindow -force-opengl

# The game's own log; Harmony and mod errors land here, not in the terminal.
logs:
	@tail -f "$(LOG)"

clean:
	cd slopd && cargo clean
	rm -f mod/Assemblies/SlopWorld.dll
	rm -rf mod/Source/SlopWorld/obj
