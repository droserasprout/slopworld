# Private aggregates. The task dispatcher selects these after validating scope.
[private]
_build-all: _daemon::build _mod::build

[private]
_format-all: _tools::format _daemon::format _mod::format

[private]
_lint-all: _tools::lint _daemon::lint _mod::lint

[private]
_test-all: _daemon::test _mod::test _mod::check-themes _tools::test _tools::test-pager

[private]
_install-all: _daemon::install _mod::install

[private]
_uninstall-all: _daemon::uninstall _mod::uninstall

[private]
_clean-all:
    cd slopd && $CARGO clean
    rm -f "$MOD_DLL"
    rm -rf mod/Source/SlopWorld/obj "$COVERAGE_DIR"
