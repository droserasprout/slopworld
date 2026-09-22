.PHONY: test-daemon test-mod test-wire-contract test-themes test-text-sprites test-prose test-bench-report test-pager coverage coverage-daemon coverage-mod coverage-summary

## Tests and coverage

.PHONY: test-tools
test-tools: test-wire-contract test-themes test-text-sprites test-prose test-bench-report ## Test supporting tools and generated data

test-daemon: api-contract ## Run Rust tests
	@cd slopd && $(CARGO) test --quiet

test-mod: protobuf-deps api-contract ## Run game-free C# tests
	@$(DOTNET) run --project "$(TEST_PROJECT)" --configuration Release -- --quiet

test-wire-contract: api-contract ## Test shared definitions and generated bindings
	@$(PYTHON) tools/test_wire_contract.py

test-themes: validate-themes ## Test theme catalog build validation
	@$(PYTHON) tools/test_validate_themes.py

test-text-sprites: ## Test generated text sprite metadata without fonts or images
	@$(PYTHON) tools/test_text_sprites.py

test-prose:        ## Test the prose linter
	@$(PYTHON) tools/test_prose_lint.py --quiet

test-bench-report: ## Test benchmark reporting
	@$(PYTHON) tools/test_bench_report.py

test-pager:       ## Test pager geometry with isolated tmux and less (no game)
	@$(PYTHON) tools/test_pager_geometry.py

coverage: coverage-daemon coverage-mod ## Measure Rust and game-free C# test coverage

coverage-daemon: api-contract ## Measure Rust coverage and write coverage/rust.cobertura.xml
	@bash tools/coverage.sh daemon

coverage-mod: protobuf-deps api-contract ## Measure game-free C# coverage and write coverage/csharp.cobertura.xml
	@bash tools/coverage.sh mod

coverage-summary: ## Summarize existing Rust and C# coverage reports
	@$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/rust.cobertura.xml" Rust
	@echo
	@$(PYTHON) tools/coverage_summary.py "$(COVERAGE_DIR)/csharp.cobertura.xml" 'C#'
