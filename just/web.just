# Build redistributable slopweb tooling; proprietary runtime stays in local output.
[group('web')]
slopweb-build: (mod-build 'rimref')
    just --justfile experiments/browser/runtime/justfile build
    dotnet build experiments/browser/runtime/Adapters/Adapters.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build experiments/browser/runtime/FontProbe/FontProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    just --justfile experiments/browser/runtime/justfile background-probe-build
    dotnet build experiments/browser/runtime/Scanner/Scanner.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build experiments/browser/compat/Offline/Offline.csproj --nologo -v quiet -p:RestoreLockedMode=true
    just slopweb-stage

# Stage already-built redistributable tools without replacing private game roots.
[group('web')]
slopweb-stage:
    uv run --locked python -m tools.release.slopweb build

[group('web')]
slopweb-check-fonts game=(env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld') output='experiments/browser/runtime/out/installed-font-check':
    python3 slopweb/out/appearance.py {{quote(game)}} {{quote(output)}}

[group('web')]
slopweb-install prefix=(env('HOME') + '/.local'): slopweb-build
    just slopweb-install-staged {{quote(prefix)}}

[group('web')]
slopweb-install-staged prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb install --prefix {{quote(prefix)}}

[group('web')]
slopweb-uninstall prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb uninstall --prefix {{quote(prefix)}}

[group('web')]
[positional-arguments]
slopweb-run *args:
    python3 slopweb/out/launch.py "$@"

# Use the desktop user's game, fonts, profile and daemon endpoint from an agent sandbox.
# A terminal gets a controlling PTY so Ctrl+C reaches the service; automation keeps pipes.
[group('web')]
[positional-arguments]
slopweb-host-run *args:
    systemd-run --user --pty --pipe --wait --collect python3 {{quote(justfile_directory() / 'slopweb/out/launch.py')}} "$@"

# Prepare an immutable AOT runtime without starting a browser or replacing a server.
# Bound compiler parallelism as well as CPU time to leave the host usable.
[group('web')]
slopweb-host-prepare-aot sdk cache:
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% --setenv=DOTNET_PROCESSOR_COUNT=2 --setenv=BINARYEN_CORES=2 python3 {{quote(justfile_directory() / 'slopweb/out/launch.py')}} --prepare --aot-sdk {{quote(sdk)}} --cache {{quote(cache)}}

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
    uv run --locked pytest tools/release/test_slopweb.py tools/release/test_slopweb_aot.py tools/release/test_slopweb_fonts.py -q

# Exercise the installed launcher and its private game preparation.
[group('web')]
slopweb-check-install prefix=(env('HOME') + '/.local'):
    {{quote(prefix + '/bin/slopweb')}} --prepare

[group('web')]
slopweb-host-check-install prefix=(env('HOME') + '/.local'):
    systemd-run --user --pipe --wait --collect {{quote(prefix + '/bin/slopweb')}} --prepare

[group('web')]
slopweb-host-check-browser runtime rendering='auto':
    systemd-run --user --pipe --wait --collect --setenv=PROBE_RENDERING={{quote(rendering)}} python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --check-installed {{quote(runtime)}}
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/installed-startup.json workspace

# Retain a prepared runtime under the same two-core bound used by workload comparisons.
[group('web')]
slopweb-host-workspace-bounded runtime rendering='auto' screenshots='0':
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% -p RuntimeMaxSec=600 --setenv=PROBE_CPU_LIMIT_CORES=2 --setenv=PROBE_RENDERING={{quote(rendering)}} --setenv=PROBE_SCREENSHOTS={{quote(screenshots)}} python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --check-installed {{quote(runtime)}} 1

# Reversible UI/font checks and real OST decoding in an owned diagnostic browser.
[group('web')]
slopweb-host-features-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_EVALUATE_TIMEOUT_MS=240000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/slopweb-features-check.js')}}

# Exercise autoplay recovery without sending input to any terminal.
[group('web')]
slopweb-host-audio-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_USER_GESTURE=1 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/slopweb-audio-check.js')}}

# Exercise stopped panes and F12 fallback on every sidebar tab in an owned browser.
[group('web')]
slopweb-host-entry-navigation-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_EVALUATE_TIMEOUT_MS=120000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/entry-navigation-check.js')}}

# Package the independent browser launcher using the selected native format.
[group('package')]
package-web distribution:
    just package-product {{quote(distribution)}} slopweb

# Inspect a local package from the current tooling bundle without restaging game data.
[group('web')]
slopweb-package-staged distribution version revision:
    uv run --locked python -m tools.release.native {{quote(distribution)}} --product slopweb --stage-only --binary-dir {{quote(justfile_directory() / 'slopweb/out')}} --output {{quote(justfile_directory() / 'dist' / distribution)}} --version {{quote(version)}} --revision {{quote(revision)}}

# Prepare/serve an explicit native snapshot without changing the live profile cache.
[group('web')]
[positional-arguments]
slopweb-host-run-portraits source *args:
    #!/usr/bin/env bash
    set -euo pipefail
    test -n {{quote(source)}}
    shift
    systemd-run --user --pipe --wait --collect --setenv=SLOPWEB_PORTRAITS={{quote(source)}} python3 {{quote(justfile_directory() / 'slopweb/out/launch.py')}} "$@"
