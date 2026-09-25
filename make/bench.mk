.PHONY: bench-daemon bench-mod bench-ipc bench-build bench-report bench-latest

## Benchmarks

bench-daemon: ## Run the daemon benchmark into shared CSV results
	@MAKE_CMD="$(MAKE_BIN)" $(PYTHON) tools/bench-report.py run --suite daemon --build "$(BUILD)" $(if $(BENCH_RUN),--run "$(BENCH_RUN)")

bench-mod: ## Run the C# helper benchmark into shared CSV results
	@MAKE_CMD="$(MAKE_BIN)" $(PYTHON) tools/bench-report.py run --suite mod --build "$(BUILD)" $(if $(BENCH_RUN),--run "$(BENCH_RUN)")

bench-ipc: ## Run production Protobuf IPC into shared CSV results
	@MAKE_CMD="$(MAKE_BIN)" $(PYTHON) tools/bench-report.py run --suite ipc --build "$(BUILD)" $(if $(BENCH_RUN),--run "$(BENCH_RUN)")

bench-build: api-contract protobuf-deps
	@bash tools/bench.sh build

bench-report: ## Render saved CSV results; BENCH_BASELINE and BENCH_MODE=relative compare runs
	@$(PYTHON) tools/bench-report.py report --run "$(BENCH_RUN)" $(if $(BENCH_BASELINE),--baseline "$(BENCH_BASELINE)") $(if $(BENCH_MODE),--mode "$(BENCH_MODE)") $(if $(BENCH_REPORT_OUTPUT),--output "$(BENCH_REPORT_OUTPUT)")

bench-latest: ## Refresh the single committable benchmark report from BENCH_RUN
	@$(PYTHON) tools/bench-report.py report --run "$(BENCH_RUN)" --latest $(if $(BENCH_FALLBACK_RUN),--fallback-run "$(BENCH_FALLBACK_RUN)")

.PHONY: bench-terminal bench-terminal-typing
bench-terminal: ## Run focused desktop terminal input into shared CSV results
	@$(PYTHON) bench/terminal-input/terminal-input-bench.py $(if $(BENCH_RUN),--run "$(BENCH_RUN)") $(if $(BENCH_PHASE),--phase "$(BENCH_PHASE)") $(if $(filter 1 true,$(BENCH_FILL_HISTORY)),--fill-history) $(if $(BENCH_MULTIPLIER),--multiplier "$(BENCH_MULTIPLIER)") $(if $(BENCH_PREPARE_SECONDS),--prepare-seconds "$(BENCH_PREPARE_SECONDS)") $(if $(BENCH_BACKEND),--backend "$(BENCH_BACKEND)") $(if $(BENCH_TRACE_LOG),--log "$(BENCH_TRACE_LOG)")

bench-terminal-typing: override BENCH_PHASE := typing
bench-terminal-typing: override BENCH_FILL_HISTORY := 1
bench-terminal-typing: bench-terminal ## Fill an empty host tab, then measure typing repaint reasons
