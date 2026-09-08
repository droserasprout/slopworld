.DEFAULT_GOAL := help

MAKEFLAGS += --no-print-directory
MAKE_BIN := $(MAKE)

##
##  🤖 SlopWorld developer tools
##
##  RIMWORLD must point at a real install; the mod builds against the game's
##  own assemblies. PROFILE picks the save data folder `run` launches into.
##  BUILD is debug or release and every target follows it, install included:
##  `make BUILD=release install`.
##

.PHONY: help
help:              ## Show this help (default)
	@awk 'BEGIN { tag = "#" "#" } index($$0, tag) == 1 { if (substr($$0, 3, 2) == "->") print "->" substr($$0, 5); else if (substr($$0, 3) != "") print substr($$0, 3); next } /^[[:alnum:]_.-]+:/ && index($$0, tag) { target = $$0; sub(/:.*/, "", target); desc = substr($$0, index($$0, tag) + 2); sub(/^[[:space:]]+/, "", desc); printf "%-18s %s\n", target ":", desc }' $(MAKEFILE_LIST)

# Keep the include order aligned with the help sections below.
include make/config.mk
include make/rimworld.mk
include make/macos.mk
include make/build.mk
include make/quality.mk
include make/install.mk
include make/run.mk
include make/misc.mk
