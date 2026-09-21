.PHONY: gogdl-login gogdl-install gogdl-update

##

gogdl-login:       ## Log into GOG interactively and save the gogdl token
	@bash tools/gogdl.sh login

gogdl-install:     ## Install RimWorld's native Linux build with gogdl
	@bash tools/gogdl.sh install

gogdl-update:      ## Update the local RimWorld copy with gogdl
	@bash tools/gogdl.sh update
