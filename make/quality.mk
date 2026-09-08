.PHONY: format-daemon format-mod lint-daemon lint-mod lint-prose

##

format-daemon:
	@cd slopd && $(CARGO) fmt

format-mod:
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj

##

lint-daemon:
	@cd slopd && $(CARGO) fmt --check
	@cd slopd && $(CARGO) clippy --all-targets -- -D warnings

lint-mod: override BUILD := release
lint-mod: override CSC_WARNINGS := -warnaserror
lint-mod: mod
	@$(DOTNET) format whitespace mod/Source/SlopWorld --folder --exclude obj --verify-no-changes

lint-prose:        ## Find LLM cliches in prose and source comments
	@$(PYTHON) tools/prose_lint.py $(PROSE_LINT_ARGS)
