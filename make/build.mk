.PHONY: daemon mod protobuf-deps validate-themes clean

## Build

daemon: api-contract ## Build the daemon and the launcher
	@cd slopd && $(if $(VERSION),SLOPWORLD_BUILD_VERSION="$(VERSION)",) $(CARGO) build $(CARGOFLAGS)

mod: daemon validate-themes protobuf-deps        ## Build the mod against the game's assemblies
	@version="$(VERSION)"; \
	if test -z "$$version"; then version="$$("$(RUNNER)" --version)" || exit; fi; \
	$(DOTNET) build "$(MOD_PROJECT)" --configuration $(if $(filter release,$(BUILD)),Release,Debug) \
		-p:RimWorldManaged="$(if $(filter /%,$(MANAGED)),$(MANAGED),$(CURDIR)/$(MANAGED))" -p:InformationalVersion="$$version" \
		-p:TreatWarningsAsErrors=$(MOD_WARNINGS_AS_ERRORS) -p:RestoreLockedMode=true

protobuf-deps: ## Restore Protobuf runtime for Unity Mono
	@$(DOTNET) build mod/Dependencies/Protobuf.csproj --configuration Release --verbosity quiet -p:RestoreLockedMode=true

validate-themes: ## Validate the shipped UI and terminal theme catalogs
	@$(PYTHON) tools/validate_themes.py

clean:             ## Remove build output
	@cd slopd && $(CARGO) clean
	@rm -f "$(MOD_DLL)"
	@rm -rf mod/Source/SlopWorld/obj
	@rm -rf "$(COVERAGE_DIR)"
