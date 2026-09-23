.PHONY: all format lint test bench install run ci

## Common commands

all: daemon mod   ## Build both halves

format: format-daemon format-csharp ## Format both halves

lint: lint-daemon lint-mod ## Lint both halves

test: test-daemon test-mod test-tools test-pager ## Run all game-free tests (requires tmux and less)

ci: coverage test-tools test-pager lint-daemon check-format-csharp check-generated ## Run game-free CI checks with coverage

bench: bench-build ## Run the game-free daemon, C# and IPC benchmarks
	@bash tools/bench.sh run

install: install-daemon install-runner install-mod install-font ## Install the daemon, runner, mod and bundled font

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")
