# Build redistributable slopweb tooling; proprietary runtime stays in local output.
[group('web')]
slopweb-build: browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    uv run --locked python -m tools.release.slopweb build

[group('web')]
slopweb-install prefix=(env('HOME') + '/.local'): slopweb-build
    uv run --locked python -m tools.release.slopweb install --prefix {{quote(prefix)}}

[group('web')]
slopweb-uninstall prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb uninstall --prefix {{quote(prefix)}}

[group('web')]
[positional-arguments]
slopweb-run *args:
    python3 slopweb/out/launch.py "$@"

[group('web')]
slopweb-format:
    uv run --locked ruff check --select I --fix slopweb tools/release
    uv run --locked ruff format slopweb tools/release

[group('web')]
slopweb-lint:
    uv run --locked ruff check slopweb tools/release
    uv run --locked ruff format --check slopweb tools/release
    uv run --locked mypy --no-error-summary tools/release/slopweb.py

[group('web')]
slopweb-test:
    uv run --locked pytest tools/release/test_slopweb.py -q

# Exercise the installed launcher and its private game preparation.
[group('web')]
slopweb-check-install prefix=(env('HOME') + '/.local'):
    {{quote(prefix + '/bin/slopweb')}} --prepare

# Package the independent browser launcher using the selected native format.
[group('package')]
package-web distribution:
    just package-product {{quote(distribution)}} slopweb
