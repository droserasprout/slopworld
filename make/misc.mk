.PHONY: logs check-reqs shot pkg-arch docs docs-serve devloop

##

logs:              ## Tail the game's Player.log
	@tail -f "$(LOG)"

check-reqs:        ## Print required and optional host requirements
	@RIMWORLD="$(RIMWORLD)" $(PYTHON) tools/check-reqs.py

# Needs the `x11` preset on this project's sandbox; see tools/shot.sh.
shot:              ## Screenshot the game window into OUT
	@tools/shot.sh $(OUT)

pkg-arch:          ## Build and install Arch package
	cd packaging/arch && makepkg -p PKGBUILD.local -sif

docs: api-docs     ## Build human docs
	cd docs && mdbook build

docs-serve:        ## Serve human docs
	cd docs && mdbook serve

devloop:           ## Reinstall and relaunch after every game exit
	MAKE_CMD="$(MAKE_BIN)" tools/devloop.sh
