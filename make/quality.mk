.PHONY: format-daemon format-csharp format-mod check-format-csharp lint-daemon lint-mod

CSHARP_FORMAT_INCLUDE := mod/Source/SlopWorld mod/Tests bench/ipc/csharp
CSHARP_FORMAT_EXCLUDE := \
	mod/Source/SlopWorld/obj \
	mod/Tests/obj \
	bench/ipc/csharp/obj \
	mod/Source/SlopWorld/Client/Generated
CSHARP_FORMAT_COMMAND = $(DOTNET) format whitespace . --folder \
	--include $(CSHARP_FORMAT_INCLUDE) \
	--exclude $(CSHARP_FORMAT_EXCLUDE)

## Formatting

format-daemon: ## Format Rust sources
	@cd slopd && $(CARGO) fmt

format-csharp: ## Format C# production, test and benchmark sources
	@$(CSHARP_FORMAT_COMMAND)

format-mod: format-csharp ## Alias for formatting all C# sources

check-format-csharp: ## Check formatting across C# production, tests and benchmarks
	@$(CSHARP_FORMAT_COMMAND) --verify-no-changes

## Lint

lint-daemon: api-contract ## Check Rust formatting and Clippy
	@cd slopd && $(CARGO) fmt --check
	@cd slopd && $(CARGO) clippy --all-targets -- -D warnings

lint-mod: override BUILD := release
lint-mod: override MOD_WARNINGS_AS_ERRORS := true
lint-mod: mod check-format-csharp ## Build and check C# formatting (requires game assemblies)
