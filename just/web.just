# Build redistributable slopweb tooling; proprietary runtime stays in local output.
[group('web')]
web-build:
    python3 slopweb/build_tools.py

# Stage already-built redistributable tools without replacing private game roots.
[group('web')]
web-stage:
    uv run --locked python -m tools.release.slopweb build

[group('web')]
web-check-fonts game=(env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld') output='slopweb/runtime/out/installed-font-check':
    python3 slopweb/out/appearance.py {{quote(game)}} {{quote(output)}}

[group('web')]
web-install prefix=(env('HOME') + '/.local'): web-build (web-install-staged prefix)

[group('web')]
web-install-staged prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb install --prefix {{quote(prefix)}}

[group('web')]
web-uninstall prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb uninstall --prefix {{quote(prefix)}}

[group('web')]
[positional-arguments]
web-run *args: web-build
    python3 slopweb/just_launch.py "$@"

# Prepare an immutable AOT runtime without starting a browser or replacing a server.
# Limit compiler parallelism; resource limits come from the calling environment.
[group('web')]
web-prepare-aot sdk cache cores='2': web-build
    @python3 -c 'import re, sys; sys.exit(0 if re.fullmatch(r"[1-9][0-9]*", sys.argv[1]) else "cores must be a positive integer")' {{quote(cores)}}
    DOTNET_PROCESSOR_COUNT={{quote(cores)}} BINARYEN_CORES={{quote(cores)}} python3 {{quote(justfile_directory() / 'slopweb/just_launch.py')}} --prepare --aot-sdk {{quote(sdk)}} --cache {{quote(cache)}}

# Read only the compiler log; do not attach to a browser or running workspace.
[group('web')]
web-aot-log key cache=(env('HOME') + '/.cache/slopweb'):
    tail -n 12 {{quote(cache / 'aot-builds' / key / 'publish.log')}}

[group('web')]
web-format:
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier format
    uv run --locked ruff check --select I --fix slopweb
    uv run --locked ruff format slopweb

[group('web')]
web-lint: web-check-types
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier check
    while IFS= read -r file; do node --check "$file"; done < <(rg --files slopweb/runtime slopweb/server slopweb/tests -g '*.js' -g '*.mjs')
    uv run --locked ruff check slopweb
    uv run --locked ruff format --check slopweb

[group('web')]
web-test: web-test-runtime web-test-server
    uv run --locked pytest slopweb/tests tools/release/test_slopweb.py -q

# Managed and Node contracts; no installed game or browser required.
[group('web')]
web-test-runtime:
    dotnet build slopweb/runtime/Adapters/Adapters.csproj --nologo -v quiet -p:RestoreLockedMode=true
    for project in AudioTests ClipboardTests DispatchTests InputTests PreferencesTests StartupTests UnderlyingTests WorkspaceFrameTests; do dotnet run --project "slopweb/tests/runtime/$project/$project.csproj" -c Release -p:RestoreLockedMode=true; done
    dotnet run --project slopweb/tests/runtime/CommandTests/CommandTests.csproj -c Release -p:RestoreLockedMode=true -- --quad-fixture slopweb/tests/runtime/CommandTests/bin/quad-fixture.json
    GUI_QUAD_FIXTURE=slopweb/tests/runtime/CommandTests/bin/quad-fixture.json node --test slopweb/tests/browser/*.test.mjs

# Installed game assemblies are required; these checks do not start the game.
[group('web')]
web-test-keybindings game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/KeyBindingsTests/KeyBindingsTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}
    node --test slopweb/tests/browser/workspace-keybindings.test.mjs

# Native font/adapter checks require the local game installation.
[group('web')]
web-test-native-text game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}

[group('web')]
web-test-native-rows mod game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}} --row-mod={{quote(mod)}}

[group('web')]
web-bench-commands:
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/CommandTests/CommandTests.csproj -c Release -- --benchmark

[group('web')]
web-bench-dispatch:
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/DispatchTests/DispatchTests.csproj -c Release -- --benchmark

# Native assemblies required; no engine or browser is started.
[group('web')]
web-bench-glyphs game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    DOTNET_TieredCompilation=0 dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -c Release -p:GameDirectory={{quote(game)}} -- {{quote(game)}} --glyph-benchmark

# Exercise the installed launcher and its private game preparation.
[group('web')]
web-check-install prefix=(env('HOME') + '/.local'):
    {{quote(prefix + '/bin/slopweb')}} --prepare

# Package the independent browser launcher using the selected native format.
[group('package')]
package-web distribution: (package-product distribution 'slopweb')

# Inspect a local package from the current tooling bundle without restaging game data.
[group('web')]
web-package-staged distribution version revision:
    uv run --locked python -m tools.release.native {{quote(distribution)}} --product slopweb --stage-only --binary-dir {{quote(justfile_directory() / 'slopweb/out')}} --output {{quote(justfile_directory() / 'dist' / distribution)}} --version {{quote(version)}} --revision {{quote(revision)}}

# Prepare/serve an explicit native snapshot without changing the live profile cache.
[group('web')]
[positional-arguments]
web-run-portraits source *args: web-build
    #!/usr/bin/env bash
    set -euo pipefail
    test -n {{quote(source)}}
    shift
    SLOPWEB_PORTRAITS={{quote(source)}} python3 {{quote(justfile_directory() / 'slopweb/just_launch.py')}} "$@"

[group('web')]
web-local-fonts-build:
    python3 slopweb/runtime/LocalFonts/build.py

[group('web')]
web-local-fonts-sdk:
    python3 slopweb/runtime/LocalFonts/build.py --install-sdk

# Native font fidelity; does not start a game or browser.
[group('web')]
web-test-local-fonts: web-local-fonts-build
    dotnet build slopweb/runtime/FontProbe/FontProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    node slopweb/tests/browser/native/local-fonts.mjs
    just web-test-native-text

# Check installed Python tools independently of repository packaging modules.
[group('web')]
web-check-types:
    MYPYPATH=slopweb uv run --locked mypy --no-error-summary --no-namespace-packages slopweb/*.py slopweb/runtime/*.py slopweb/runtime/LocalFonts/build.py

[group('web')]
web-test-server:
    node --test slopweb/tests/server/*.test.mjs

# Restore every reviewed browser project lock without installed game assemblies.
[group('web')]
web-restore:
    for project in $(rg --files slopweb -g '*.csproj' | sort); do dotnet restore "$project" --locked-mode --nologo -v quiet; done

# Remove authored build output; installed caches and private research are separate.
[group('web')]
web-clean:
    uv run --locked python -m tools.web clean

# Compile native fidelity checks against installed assemblies without executing them.
[group('web')]
web-check-native-build game=env('RIMWORLD', env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld'):
    dotnet build slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj --nologo -v quiet -p:RestoreLockedMode=true -p:GameDirectory={{quote(game)}}
    dotnet build slopweb/tests/runtime/KeyBindingsTests/KeyBindingsTests.csproj --nologo -v quiet -p:RestoreLockedMode=true -p:GameDirectory={{quote(game)}}

# Installed fonts/assemblies only: exercise on-demand file delivery without a browser.
[group('web')]
web-test-font-delivery: web-build
    node slopweb/tests/browser/native/font-delivery.mjs
