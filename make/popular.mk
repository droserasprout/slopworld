.PHONY: all format lint test bench bench-daemon install run

##

all: daemon mod   ## Build both halves

format: format-daemon format-mod ## Format both halves

lint: lint-daemon lint-mod ## Lint both halves

test: test-daemon test-mod test-prose test-bench-report ## Run the daemon and game-free mod tests

bench: bench-build ## Run the game-free daemon, C# and IPC benchmarks
	@bash tools/bench.sh run

install: install-daemon install-runner install-mod install-font ## Install the daemon, runner, mod and bundled font

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")
