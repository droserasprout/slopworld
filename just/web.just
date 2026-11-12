# Build redistributable slopweb tooling; proprietary runtime stays in local output.
[group('web')]
slopweb-build: (mod-build 'rimref') && slopweb-stage
    python3 slopweb/runtime/LocalFonts/build.py
    dotnet build slopweb/runtime/Probe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build slopweb/runtime/Adapters/Adapters.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build slopweb/runtime/FontProbe/FontProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build slopweb/runtime/BackgroundProbe/BackgroundProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build slopweb/runtime/Scanner/Scanner.csproj --nologo -v quiet -p:RestoreLockedMode=true
    dotnet build slopweb/offline/Offline.csproj --nologo -v quiet -p:RestoreLockedMode=true

# Stage already-built redistributable tools without replacing private game roots.
[group('web')]
slopweb-stage:
    uv run --locked python -m tools.release.slopweb build

[group('web')]
slopweb-check-fonts game=(env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld') output='slopweb/runtime/out/installed-font-check':
    python3 slopweb/out/appearance.py {{quote(game)}} {{quote(output)}}

[group('web')]
slopweb-install prefix=(env('HOME') + '/.local'): slopweb-build (slopweb-install-staged prefix)

[group('web')]
slopweb-install-staged prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb install --prefix {{quote(prefix)}}

[group('web')]
slopweb-uninstall prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb uninstall --prefix {{quote(prefix)}}

[group('web')]
[positional-arguments]
slopweb-run *args:
    python3 slopweb/just_launch.py "$@"

# Run directly in the calling terminal and environment.
[group('web')]
[positional-arguments]
slopweb-host-run *args:
    python3 {{quote(justfile_directory() / 'slopweb/just_launch.py')}} "$@"

# Prepare an immutable AOT runtime without starting a browser or replacing a server.
# Limit compiler parallelism; resource limits come from the calling environment.
[group('web')]
slopweb-host-prepare-aot sdk cache cores='2':
    @python3 -c 'import re, sys; sys.exit(0 if re.fullmatch(r"[1-9][0-9]*", sys.argv[1]) else "cores must be a positive integer")' {{quote(cores)}}
    DOTNET_PROCESSOR_COUNT={{quote(cores)}} BINARYEN_CORES={{quote(cores)}} python3 {{quote(justfile_directory() / 'slopweb/just_launch.py')}} --prepare --aot-sdk {{quote(sdk)}} --cache {{quote(cache)}}

# Read only the compiler log; do not attach to a browser or running workspace.
[group('web')]
slopweb-host-aot-log key cache=(env('HOME') + '/.cache/slopweb'):
    tail -n 12 {{quote(cache / 'aot-builds' / key / 'publish.log')}}

[group('web')]
slopweb-format:
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier format
    uv run --locked ruff check --select I --fix slopweb tools/release
    uv run --locked ruff format slopweb tools/release

[group('web')]
slopweb-lint:
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier check
    while IFS= read -r file; do node --check "$file"; done < <(rg --files slopweb/runtime slopweb/server slopweb/tests -g '*.js' -g '*.mjs')
    uv run --locked ruff check slopweb tools/release
    uv run --locked ruff format --check slopweb tools/release
    uv run --locked mypy --no-error-summary tools/release/slopweb.py

[group('web')]
slopweb-test: slopweb-test-runtime
    node --test slopweb/tests/server/*.test.mjs
    uv run --locked pytest slopweb/tests tools/release/test_slopweb.py -q

# Managed and Node contracts; no installed game or browser required.
[group('web')]
slopweb-test-runtime:
    dotnet build slopweb/runtime/Adapters/Adapters.csproj --nologo -v quiet -p:RestoreLockedMode=true
    for project in AudioTests ClipboardTests DispatchTests InputTests PreferencesTests StartupTests UnderlyingTests WorkspaceFrameTests; do dotnet run --project "slopweb/tests/runtime/$project/$project.csproj" -c Release -p:RestoreLockedMode=true; done
    dotnet run --project slopweb/tests/runtime/CommandTests/CommandTests.csproj -c Release -p:RestoreLockedMode=true -- --quad-fixture slopweb/tests/runtime/CommandTests/bin/quad-fixture.json
    GUI_QUAD_FIXTURE=slopweb/tests/runtime/CommandTests/bin/quad-fixture.json node --test slopweb/tests/browser/*.test.mjs

# Installed game assemblies are required; these checks do not start the game.
[group('web')]
slopweb-test-keybindings game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/KeyBindingsTests/KeyBindingsTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}
    node --test slopweb/tests/browser/workspace-keybindings.test.mjs

# Native font/adapter checks require the local game installation.
[group('web')]
slopweb-test-native-text game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}

[group('web')]
slopweb-test-native-rows mod game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}} --row-mod={{quote(mod)}}

[group('web')]
slopweb-bench-commands:
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/CommandTests/CommandTests.csproj -c Release -- --benchmark

[group('web')]
slopweb-bench-dispatch:
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/DispatchTests/DispatchTests.csproj -c Release -- --benchmark

# Native assemblies required; no engine or browser is started.
[group('web')]
slopweb-bench-glyphs game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -c Release -p:GameDirectory={{quote(game)}} -- {{quote(game)}} --glyph-benchmark

# Exercise the installed launcher and its private game preparation.
[group('web')]
slopweb-check-install prefix=(env('HOME') + '/.local'):
    {{quote(prefix + '/bin/slopweb')}} --prepare

[group('web')]
slopweb-host-check-install prefix=(env('HOME') + '/.local'): (slopweb-check-install prefix)

# Package the independent browser launcher using the selected native format.
[group('package')]
package-web distribution: (package-product distribution 'slopweb')

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
    SLOPWEB_PORTRAITS={{quote(source)}} python3 {{quote(justfile_directory() / 'slopweb/just_launch.py')}} "$@"

[group('web')]
slopweb-local-fonts-build:
    python3 slopweb/runtime/LocalFonts/build.py

[group('web')]
slopweb-local-fonts-sdk:
    python3 slopweb/runtime/LocalFonts/build.py --install-sdk

# Native font fidelity; does not start a game or browser.
[group('web')]
slopweb-test-local-fonts: slopweb-local-fonts-build
    dotnet build slopweb/runtime/FontProbe/FontProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    node slopweb/tests/browser/native/local-fonts.mjs
    just slopweb-test-native-text
