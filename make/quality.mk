.PHONY: format format-daemon format-mod lint lint-daemon lint-mod lint-prose

##
##-> Format and lint
##

format: format-daemon format-mod ## Format both halves

format-daemon:     ## rustfmt the daemon
	@cd slopd && $(CARGO) fmt

format-mod:        ## Format the mod's C# (needs the .NET SDK)
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj

##

lint: lint-daemon lint-mod ## Lint both halves

lint-daemon:       ## Check the daemon's formatting, then clippy, warnings as errors
	@cd slopd && $(CARGO) fmt --check
	@cd slopd && $(CARGO) clippy --all-targets -- -D warnings

lint-mod: override BUILD := release
lint-mod: override CSC_WARNINGS := -warnaserror
lint-mod: mod       ## Build the mod with warnings as errors, then check its formatting
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj --verify-no-changes

lint-prose:        ## Find LLM cliches in prose and source comments
	@$(PYTHON) tools/prose_lint.py $(PROSE_LINT_ARGS)
