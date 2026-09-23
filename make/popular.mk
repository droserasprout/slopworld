.PHONY: all format lint test bench install run ci

## Common commands

all: daemon mod   ## Build the daemon and mod

format: format-daemon format-csharp ## Format Rust and C# sources

lint: lint-daemon lint-mod ## Check Rust and C# sources

test: test-daemon test-mod test-tools test-pager ## Run all game-free tests (requires tmux and less)

ci: coverage test-tools test-pager lint-daemon check-format-csharp check-generated ## Run game-free CI checks with coverage

bench: bench-build ## Run the game-free daemon, C# and IPC benchmarks
	@bash tools/bench.sh run

install: install-daemon install-runner install-mod install-font ## Install the daemon, runner, mod and bundled font

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")
