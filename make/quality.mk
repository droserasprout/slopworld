.PHONY: format-daemon format-mod lint-daemon lint-mod lint-prose

## Formatting

format-daemon: ## Format Rust sources
	@cd slopd && $(CARGO) fmt

format-mod: ## Format C# sources
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj Client/Generated

## Lint

lint-daemon: api-contract ## Check Rust formatting and Clippy
	@cd slopd && $(CARGO) fmt --check
	@cd slopd && $(CARGO) clippy --all-targets -- -D warnings

lint-mod: override BUILD := release
lint-mod: override MOD_WARNINGS_AS_ERRORS := true
lint-mod: mod ## Build and check C# formatting (requires game assemblies)
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj Client/Generated --verify-no-changes

lint-prose:        ## Find LLM cliches in prose and source comments
	@$(PYTHON) tools/prose_lint.py $(PROSE_LINT_ARGS)
