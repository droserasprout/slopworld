.PHONY: daemon mod validate-themes bench-daemon bench-report test-wire-contract test-daemon test-mod coverage coverage-daemon coverage-mod test-prose \
	reference api-contract api-docs clean

##

daemon: api-contract ## Build the daemon and the launcher
	@cd slopd && $(if $(VERSION),SLOPWORLD_BUILD_VERSION="$(VERSION)",) $(CARGO) build $(CARGOFLAGS)

bench-daemon: api-contract ## Run the game-free daemon performance benchmark
	@cd slopd && $(CARGO) run --quiet --bin slopd $(CARGOFLAGS) -- --perf-bench

bench-report: BUILD := release
bench-report:        ## Run the full performance suite three times and write an averaged note
	@$(PYTHON) tools/bench-report.py --build "$(BUILD)"

.PHONY: bench-mod
bench-mod: api-contract ## Benchmark C# helpers without RimWorld or Unity
	@DOTNET_TieredCompilation=0 $(DOTNET) run --project "$(TEST_PROJECT)" --configuration $(if $(filter release,$(BUILD)),Release,Debug) -- --perf-bench

test-wire-contract: api-contract ## Test shared definitions and generated bindings
	@$(PYTHON) tools/test_wire_contract.py

validate-themes: ## Validate the shipped UI and terminal theme catalogs
	@$(PYTHON) tools/validate_themes.py

.PHONY: test-themes
test-themes: validate-themes ## Test theme catalog build validation
	@$(PYTHON) tools/test_validate_themes.py

mod: daemon validate-themes protobuf-deps        ## Build the mod against the game's assemblies
	@version="$(VERSION)"; \
	if test -z "$$version"; then version="$$("$(RUNNER)" --version)" || exit; fi; \
	$(DOTNET) build "$(MOD_PROJECT)" --configuration $(if $(filter release,$(BUILD)),Release,Debug) \
		-p:RimWorldManaged="$(if $(filter /%,$(MANAGED)),$(MANAGED),$(CURDIR)/$(MANAGED))" -p:InformationalVersion="$$version" \
		-p:TreatWarningsAsErrors=$(MOD_WARNINGS_AS_ERRORS) -p:RestoreLockedMode=true

test-daemon: api-contract test-wire-contract
	@cd slopd && $(CARGO) test --quiet

test-mod: protobuf-deps api-contract test-wire-contract test-themes test-text-sprites
	@$(DOTNET) run --project "$(TEST_PROJECT)" --configuration Release -- --quiet

.PHONY: test-pager
test-pager:       ## Test pager geometry with isolated tmux and less (no game)
	@$(PYTHON) tools/test_pager_geometry.py

coverage: coverage-daemon coverage-mod ## Measure Rust and game-free C# test coverage

coverage-daemon: ## Measure Rust coverage and write coverage/rust.cobertura.xml
	@bash tools/coverage.sh daemon

coverage-mod: ## Measure game-free C# coverage and write coverage/csharp.cobertura.xml
	@bash tools/coverage.sh mod

test-prose:        ## Test the prose linter
	@$(PYTHON) tools/test_prose_lint.py --quiet

.PHONY: test-text-sprites
test-text-sprites: ## Test generated text sprite metadata without fonts or images
	@$(PYTHON) tools/test_text_sprites.py

reference:         ## Generate the environment/API/CLI reference
	@$(PYTHON) tools/reference.py

api-contract: shared/protocol.yaml shared/slopworld.proto tools/wire_contract.py ## Generate stable shared protocol bindings
	@mkdir -p mod/Source/SlopWorld/Client/Generated
	@protoc -I shared --csharp_out=mod/Source/SlopWorld/Client/Generated shared/slopworld.proto
	@$(PYTHON) tools/wire_contract.py
	@$(PYTHON) tools/protobuf_http.py

api-docs: api-contract ## Generate the mdBook API route inventory
	@$(PYTHON) tools/api_docs.py

clean:             ## Drop build output
	@cd slopd && $(CARGO) clean
	@rm -f "$(MOD_DLL)"
	@rm -rf mod/Source/SlopWorld/obj
	@rm -rf "$(COVERAGE_DIR)"

.PHONY: protobuf-deps
protobuf-deps: ## Restore Protobuf runtime for Unity Mono
	@$(DOTNET) build mod/Dependencies/Protobuf.csproj --configuration Release --verbosity quiet -p:RestoreLockedMode=true

.PHONY: bench-ipc
bench-ipc: protobuf-deps api-contract ## Measure production Protobuf IPC without the game
	@bash tools/bench-ipc.sh
