set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Build against the selected game's assemblies
build: check-themes restore
    #!/usr/bin/env bash
    set -euo pipefail
    version="$VERSION"
    if [ -z "$version" ]; then version=$($PYTHON -m tools.version.mod_version); fi
    managed="$MANAGED"
    if [[ "$managed" != /* ]]; then managed="$PWD/$managed"; fi
    configuration=Debug
    if [ "$BUILD" = release ]; then configuration=Release; fi
    $DOTNET build "$MOD_PROJECT" --configuration "$configuration" -p:RimWorldManaged="$managed" -p:InformationalVersion="$version" -p:TreatWarningsAsErrors="$MOD_WARNINGS_AS_ERRORS" -p:RestoreLockedMode=true

# Restore locked Unity Mono runtime packages
restore:
    $DOTNET build mod/Dependencies/Runtime.csproj --configuration Release --verbosity quiet -p:RestoreLockedMode="$MOD_DEPS_LOCKED"

[private]
restore-tools:
    $DOTNET tool restore

# Validate shipped theme catalogs
check-themes:
    $PYTHON -m tools.themes.validate_themes

# Format production, test and benchmark C# sources
format: restore-tools
    for source in $CSHARP_FORMAT_FOLDERS; do (cd "$source" && $DOTNET csharpier format .) || exit "$?"; done

# Check C# formatting without game assemblies
lint: restore-tools
    for source in $CSHARP_FORMAT_FOLDERS; do (cd "$source" && $DOTNET csharpier check .) || exit "$?"; done


# Validate a Release build with warnings as errors (requires game assemblies)
check-build:
    "$JUST_CMD" --justfile "$JUST_ROOT" BUILD=release MOD_WARNINGS_AS_ERRORS=true build mod

# Run game-free C# tests
[positional-arguments]
test *args: restore
    $DOTNET run --project "$TEST_PROJECT" --configuration Release -p:RestoreLockedMode=true -- --quiet "$@"

# Collect game-free C# coverage
coverage: restore
    $PYTHON -m tools.coverage.collect mod

# Build and install the mod (also builds its launcher)
install: launcher build
    $PYTHON -m tools.host.install_mod --runner "$RUNNER" --mods "$MODS"

[private]
launcher:
    "$JUST_CMD" --justfile "$JUST_ROOT" build daemon

# Remove the installed mod
uninstall: launcher
    "$RUNNER" mod uninstall --mods "$MODS"

# Remove component build output
clean:
    rm -f "$MOD_DLL"
    rm -rf mod/Source/SlopWorld/obj
