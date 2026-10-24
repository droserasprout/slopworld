use super::*;
use clap::error::ErrorKind;

#[test]
fn no_arguments_select_service_and_perf_bench_selects_benchmarks() {
    assert!(!Args::try_parse_from(["slopd"]).unwrap().perf_bench);
    assert!(
        Args::try_parse_from(["slopd", "--perf-bench"])
            .unwrap()
            .perf_bench
    );
}

#[test]
fn invalid_arguments_do_not_select_service_or_benchmarks() {
    for arguments in [
        vec!["slopd", "--unknown"],
        vec!["slopd", "start"],
        vec!["slopd", "--perf-bench", "extra"],
        vec!["slopd", "--perf-bench=true"],
        vec!["slopd", "--perf-bench", "--perf-bench"],
    ] {
        let error = Args::try_parse_from(arguments).unwrap_err();
        assert_eq!(error.exit_code(), 2);
    }
}

#[test]
fn help_and_version_short_circuit_benchmark_selection() {
    for flag in ["--help", "-h", "--version", "-V"] {
        let error = Args::try_parse_from(["slopd", "--perf-bench", flag]).unwrap_err();
        assert!(matches!(
            error.kind(),
            ErrorKind::DisplayHelp | ErrorKind::DisplayVersion
        ));
        assert_eq!(error.exit_code(), 0);
    }
}
