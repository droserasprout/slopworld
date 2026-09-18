.PHONY: all format lint test bench bench-daemon install run

##

all: daemon mod   ## Build both halves

format: format-daemon format-mod ## Format both halves

lint: lint-daemon lint-mod ## Lint both halves

test: test-daemon test-mod test-prose ## Run the daemon and game-free mod tests

.NOTPARALLEL: bench
bench: bench-daemon bench-mod bench-ipc ## Run the game-free daemon, C# and IPC benchmarks

install: install-daemon install-runner install-mod install-font ## Install the daemon, runner, mod and bundled font

run: daemon        ## Launch the game through the runner
	$(RUNNER) --game "$(RIMWORLD)" $(if $(PROFILE),--profile "$(PROFILE)")
