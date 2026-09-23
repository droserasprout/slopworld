.DEFAULT_GOAL := help

MAKEFLAGS += --no-print-directory
MAKE_BIN := $(MAKE)

#
##  🤖 SlopWorld developer tools
#
##  RIMWORLD must identify a real game installation. The mod uses its assemblies.
##  PROFILE selects the save data folder for `run`.
##  BUILD is debug or release. It applies to every target, including install:
##  `make BUILD=release install`.
#

.PHONY: help
help:              ## Show this help (default)
	@awk 'BEGIN { tag = "#" "#" } index($$0, tag) == 1 { if (substr($$0, 3) != "") print "\n" substr($$0, 3); else print ""; next } /^[[:alnum:]_.-]+:/ && index($$0, tag) { target = $$0; sub(/:.*/, "", target); desc = substr($$0, index($$0, tag) + 2); sub(/^[[:space:]]+/, "", desc); printf "%-22s %s\n", target ":", desc }' $(MAKEFILE_LIST)

# Keep the include order aligned with the help sections below: popular commands first.
include make/config.mk
include make/popular.mk
include make/build.mk
include make/test.mk
include make/bench.mk
include make/generate.mk
include make/quality.mk
include make/install.mk
include make/sidecar.mk
include make/macos.mk
include make/misc.mk
include make/rimworld.mk
