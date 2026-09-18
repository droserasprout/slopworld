.PHONY: daemon mod validate-themes bench-daemon bench-report loc-report test-wire-contract test-daemon test-mod coverage coverage-daemon coverage-mod test-prose \
	appicon icons emoji-atlas reference api-contract api-docs scheme-report harmony clean

##

daemon: api-contract ## Build the daemon and the launcher
	@cd slopd && $(if $(VERSION),SLOPWORLD_BUILD_VERSION="$(VERSION)",) $(CARGO) build $(CARGOFLAGS)

bench-daemon: api-contract ## Run the game-free daemon performance benchmark
	@cd slopd && $(CARGO) run --quiet --bin slopd $(CARGOFLAGS) -- --perf-bench

bench-report: BUILD := release
bench-report:        ## Run the full performance suite three times and write an averaged note
	@$(PYTHON) tools/bench-report.py --build "$(BUILD)"

loc-report:          ## Measure Python, C#, and Rust lines and write a dated note
	@$(PYTHON) tools/loc-report.py $(LOC_REPORT_ARGS)

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
	@test -f "$(CSC_API)/mscorlib.dll" || { echo "missing Mono reference assemblies under $(CSC_API)" >&2; exit 1; }
	@test -f "$(MANAGED)/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under $(MANAGED)" >&2; exit 1; }
	@version="$(VERSION)"; \
	if test -z "$$version"; then version="$$($(RUNNER) --version)"; fi; \
	mkdir -p "$(dir $(MOD_ASSEMBLY_INFO))"; \
	{ \
		printf '%s\n' \
			'using System.Reflection;' \
			"[assembly: AssemblyInformationalVersion(\"$$version\")]"; \
	} > "$(MOD_ASSEMBLY_INFO)"
	@$(CSC) -nologo -noconfig -target:library -langversion:latest \
		-out:"$(MOD_DLL)" $(CSC_OPTIMIZE) $(CSC_WARNINGS) \
		$(CSC_REFS) "$(MOD_ASSEMBLY_INFO)" $(CSC_SOURCES)

test-daemon: api-contract test-wire-contract
	@cd slopd && $(CARGO) test --quiet

test-mod: protobuf-deps api-contract test-wire-contract test-themes
	@$(DOTNET) run --project "$(TEST_PROJECT)" --configuration Release -- --quiet

.PHONY: test-pager
test-pager:       ## Test pager geometry with isolated tmux and less (no game)
	@$(PYTHON) tools/test_pager_geometry.py

coverage: coverage-daemon coverage-mod ## Measure Rust and game-free C# test coverage

coverage-daemon:
	@command -v cargo-llvm-cov >/dev/null || { echo "missing cargo-llvm-cov; install it with: cargo install cargo-llvm-cov --locked" >&2; exit 1; }
	@command -v llvm-cov >/dev/null && command -v llvm-profdata >/dev/null || { echo "missing LLVM coverage tools" >&2; exit 1; }
	@mkdir -p "$(COVERAGE_DIR)"
	@cd slopd && LLVM_COV="$$(command -v llvm-cov)" LLVM_PROFDATA="$$(command -v llvm-profdata)" \
		$(CARGO) llvm-cov --cobertura --output-path "../$(COVERAGE_DIR)/rust.cobertura.xml"
	@$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/rust.cobertura.xml" Rust

coverage-mod:
	@$(DOTNET) tool restore
	@mkdir -p "$(COVERAGE_DIR)"
	@$(DOTNET) build "$(TEST_PROJECT)" --configuration Release -p:Coverage=true
	@$(DOTNET) tool run coverlet -- "$(TEST_DLL)" \
		--target dotnet --targetargs "$(TEST_DLL) --quiet" \
		--include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' \
		--format cobertura --output "$(COVERAGE_DIR)/csharp.cobertura.xml"
	@$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/csharp.cobertura.xml" C\#

test-prose:        ## Test the prose linter
	@$(PYTHON) tools/test_prose_lint.py --quiet

appicon:           ## Regenerate the app icon (robot face + wilted rose)
	@$(PYTHON) tools/appicon.py

icons:             ## Rebake the action icons from a Nerd Font's Codicons
	@$(PYTHON) tools/icons.py

emoji-atlas:       ## Rebake the legacy terminal's emoji atlas with Pango
	@$(PYTHON) tools/emoji_atlas.py

reference:         ## Generate the environment/API/CLI reference
	@$(PYTHON) tools/reference.py

api-contract: shared/protocol.yaml shared/slopworld.proto tools/wire_contract.py ## Generate stable shared protocol bindings
	@mkdir -p mod/Source/SlopWorld/Client/Generated
	@protoc -I shared --csharp_out=mod/Source/SlopWorld/Client/Generated shared/slopworld.proto
	@$(PYTHON) tools/wire_contract.py

api-docs: api-contract ## Generate the mdBook API route inventory
	@$(PYTHON) tools/api_docs.py

scheme-report:     ## Analyze the complete UI schemes and check Warm's luminance hierarchy
	@$(PYTHON) tools/analyze_ui_schemes.py --check-warm

harmony:           ## Fetch the latest Harmony release into the mod
	@tools/fetch-harmony.sh

clean:             ## Drop build output
	@cd slopd && $(CARGO) clean
	@rm -f "$(MOD_DLL)"
	@rm -rf mod/Source/SlopWorld/obj
	@rm -rf "$(COVERAGE_DIR)"

.PHONY: protobuf-deps
protobuf-deps: ## Restore Protobuf runtime for Unity Mono
	@$(DOTNET) build mod/Dependencies/Protobuf.csproj --configuration Release --verbosity quiet
