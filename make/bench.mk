.PHONY: bench-daemon bench-mod bench-ipc bench-build bench-report

## Benchmarks

bench-daemon: api-contract ## Run the game-free daemon performance benchmark
	@cd slopd && $(CARGO) run --quiet --bin slopd $(CARGOFLAGS) -- --perf-bench

bench-mod: protobuf-deps api-contract ## Benchmark C# helpers without RimWorld or Unity
	@DOTNET_TieredCompilation=0 $(DOTNET) run --project "$(TEST_PROJECT)" --configuration $(if $(filter release,$(BUILD)),Release,Debug) -- --perf-bench

bench-ipc: protobuf-deps api-contract ## Measure production Protobuf IPC without the game
	@bash tools/bench-ipc.sh

bench-build: api-contract protobuf-deps
	@bash tools/bench.sh build

bench-report: BUILD := release
bench-report:        ## Run the full performance suite three times and write medians and ranges
	@MAKE_CMD="$(MAKE_BIN)" $(PYTHON) tools/bench-report.py --build "$(BUILD)" $(if $(BENCH_REPORT_OUTPUT),--output "$(BENCH_REPORT_OUTPUT)")
