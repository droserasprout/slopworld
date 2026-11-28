web_compiler_cache := env('HOME') / '.cache/slopweb/compiler-cache'
web_host_manifest := 'slopweb/host/Cargo.toml'

# Build redistributable slopweb tooling; proprietary runtime stays in local output.
[group('web')]
web-build:
    uv run --locked python -m tools.web_build

# Stage already-built redistributable tools without replacing private game roots.
[group('web')]
web-stage:
    uv run --locked python -m tools.release.slopweb build

[group('web')]
web-install prefix=(env('HOME') + '/.local'): web-build
    uv run --locked python -m tools.release.slopweb install --prefix {{quote(prefix)}}

[group('web')]
web-uninstall prefix=(env('HOME') + '/.local'):
    uv run --locked python -m tools.release.slopweb uninstall --prefix {{quote(prefix)}}

# Package the independent browser launcher using the selected native format.
[group('package')]
package-web distribution: (package-product distribution 'slopweb')

# Inspect a local package from the current tooling bundle without restaging game data.
[group('web')]
web-package-staged distribution version revision:
    uv run --locked python -m tools.release.native {{quote(distribution)}} --product slopweb --stage-only --binary-dir {{quote(justfile_directory() / 'slopweb/out')}} --output {{quote(justfile_directory() / 'dist' / distribution)}} --version {{quote(version)}} --revision {{quote(revision)}}

[group('web')]
[positional-arguments]
web-run *args: web-build
    slopweb/out/slopweb-host --ensure-workload "$@"

# Run the staged interpreter build without rebuilding; reuse the shared compiler cache.
[group('web')]
web-start port='8857' cache=web_compiler_cache:
    slopweb/out/slopweb-host --interpreted --port {{quote(port)}} --cache {{quote(cache)}}

# Run the staged AOT build without rebuilding; prepare AOT only on a cache miss.
[group('web')]
web-start-aot port='8857' cache=web_compiler_cache:
    slopweb/out/slopweb-host --ensure-workload --aot --port {{quote(port)}} --cache {{quote(cache)}}

# Prepare an immutable AOT runtime without starting a browser or replacing a server.
[group('web')]
web-prepare-aot sdk cache cores='8': web-build (web-prepare-aot-staged sdk cache cores)

# Prepare from the existing staged bundle with the same compiler limits.
[group('web')]
web-prepare-aot-staged sdk cache cores='8':
    @python3 -c 'import re, sys; sys.exit(0 if re.fullmatch(r"[1-9][0-9]*", sys.argv[1]) else "cores must be a positive integer")' {{quote(cores)}}
    DOTNET_PROCESSOR_COUNT={{quote(cores)}} BINARYEN_CORES={{quote(cores)}} EMCC_CORES={{quote(cores)}} {{quote(justfile_directory() / 'slopweb/out/slopweb-host')}} --ensure-workload --prepare --aot-sdk {{quote(sdk)}} --cache {{quote(cache)}}

[group('web')]
web-format:
    cargo fmt --manifest-path {{web_host_manifest}}
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier format
    uv run --locked ruff check --select I --fix slopweb
    uv run --locked ruff format slopweb

[group('web')]
web-lint:
    cargo fmt --manifest-path {{web_host_manifest}} --check
    cargo clippy --manifest-path {{web_host_manifest}} --locked --all-targets --quiet -- -D warnings
    uv run --locked mypy --no-error-summary slopweb/runtime/LocalFonts/build.py
    rg --files slopweb/runtime slopweb/offline slopweb/tests/runtime -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier check
    while IFS= read -r file; do node --check "$file"; done < <(rg --files slopweb/runtime slopweb/tests -g '*.js' -g '*.mjs')
    uv run --locked ruff check slopweb
    uv run --locked ruff format --check slopweb

# Restore every reviewed browser project lock without installed game assemblies.
[group('web')]
web-restore:
    cargo fetch --manifest-path {{web_host_manifest}} --locked
    for project in $(rg --files slopweb -g '*.csproj' | sort); do dotnet restore "$project" --locked-mode --nologo -v quiet; done

# Remove authored build output; installed caches and private research are separate.
[group('web')]
web-clean:
    cargo clean -p slopweb
    uv run --locked python -m tools.web clean

[group('web')]
web-test: web-host-build web-host-test web-test-runtime
    uv run --locked pytest tools/release/test_slopweb.py tools/test_web_local_fonts.py -q

# Managed and Node contracts; no installed game or browser required.
[group('web')]
web-test-runtime: (mod-build "rimref")
    dotnet build slopweb/runtime/Adapters/Adapters.csproj --nologo -v quiet -p:RestoreLockedMode=true
    for project in AudioTests ClipboardTests DispatchTests ImageTests InputTests PreferencesTests StartupTests UnderlyingTests WorkspaceFrameTests; do dotnet run --project "slopweb/tests/runtime/$project/$project.csproj" -c Release -p:RestoreLockedMode=true; done
    dotnet run --project slopweb/tests/runtime/CommandTests/CommandTests.csproj -c Release -p:RestoreLockedMode=true -- --quad-fixture slopweb/tests/runtime/CommandTests/bin/quad-fixture.json
    GUI_QUAD_FIXTURE=slopweb/tests/runtime/CommandTests/bin/quad-fixture.json node --test slopweb/tests/browser/*.test.mjs

# Compile native fidelity checks against installed assemblies without executing them.
[group('web')]
web-check-native-build game=RIMWORLD: (mod-build "rimref")
    dotnet build slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj --nologo -v quiet -p:RestoreLockedMode=true -p:GameDirectory={{quote(game)}}
    dotnet build slopweb/tests/runtime/KeyBindingsTests/KeyBindingsTests.csproj --nologo -v quiet -p:RestoreLockedMode=true -p:GameDirectory={{quote(game)}}

# Installed game assemblies are required; these checks do not start the game.
[group('web')]
web-test-keybindings game=RIMWORLD:
    dotnet run --project slopweb/tests/runtime/KeyBindingsTests/KeyBindingsTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}
    node --test slopweb/tests/browser/workspace-keybindings.test.mjs

# Native font/adapter checks require the local game installation.
[group('web')]
web-test-native-text game=RIMWORLD: (mod-build "rimref")
    dotnet run --project slopweb/tests/runtime/NativeTextTests/NativeTextTests.csproj -p:GameDirectory={{quote(game)}} -- {{quote(game)}}

[group('web')]
web-local-fonts-sdk:
    python3 slopweb/runtime/LocalFonts/build.py --install-sdk

[group('web')]
web-local-fonts-build:
    python3 slopweb/runtime/LocalFonts/build.py

# Native font fidelity; does not start a game or browser.
[group('web')]
web-test-local-fonts: web-local-fonts-build
    dotnet build slopweb/runtime/FontProbe/FontProbe.csproj --nologo -v quiet -p:RestoreLockedMode=true
    node slopweb/tests/browser/native/local-fonts.mjs
    just web-test-native-text

# Exercise a running native host and local rasterizer without a browser.
[group('web')]
web-test-font-delivery url='http://127.0.0.1:8787': web-local-fonts-build
    node slopweb/tests/browser/native/font-delivery.mjs {{quote(url)}}

[group('web')]
web-host-build:
    cargo build --manifest-path {{web_host_manifest}} --locked --quiet

[group('web')]
web-host-release:
    cargo build --manifest-path {{web_host_manifest}} --target-dir target --locked --release --quiet

[group('web')]
web-host-test *args:
    cargo test --manifest-path {{web_host_manifest}} --locked --quiet {{args}}
