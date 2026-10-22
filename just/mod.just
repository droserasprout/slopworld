# Build against game assemblies, or pass managed='rimref' for pinned stubs
[group('mod')]
mod-build $managed='' $configuration='' $warnings='true': mod-check-themes mod-restore
    #!/usr/bin/env bash
    set -euo pipefail
    version="$VERSION"
    if [ -z "$version" ]; then version=$(uv run --locked python -m tools.version.mod_version); fi
    managed="${managed:-$MANAGED}"
    configuration="${configuration:-$BUILD}"
    if [ "$managed" = rimref ]; then
        references=(-p:UseRimRef=true)
    else
        if [[ "$managed" != /* ]]; then managed="$PWD/$managed"; fi
        references=(-p:UseRimRef=false "-p:RimWorldManaged=$managed")
    fi
    if [ "$configuration" = release ]; then configuration=Release; else configuration=Debug; fi
    dotnet build "mod/Source/SlopWorld/SlopWorld.csproj" --verbosity quiet --nologo --configuration "$configuration" "${references[@]}" -p:InformationalVersion="$version" -p:TreatWarningsAsErrors="$warnings" -p:RestoreLockedMode=true

# Restore locked Unity Mono runtime packages
[group('mod')]
mod-restore:
    dotnet build mod/Dependencies/Runtime.csproj --configuration Release --verbosity quiet --nologo -p:RestoreLockedMode=true

[private]
[group('mod')]
mod-restore-tools:
    dotnet tool restore --verbosity quiet

# Validate shipped theme catalogs
[group('mod')]
mod-check-themes:
    uv run --locked python -m tools.themes.validate_themes

# Format production, test and benchmark C# sources
[group('mod')]
mod-format: mod-restore-tools
    for source in mod/Source/SlopWorld mod/Tests bench/ipc/csharp bench/terminal-input/tests/http_transport; do (cd "$source" && dotnet csharpier format --log-level Warning .) || exit "$?"; done

# Check C# formatting without game assemblies
[group('mod')]
mod-lint: mod-restore-tools
    for source in mod/Source/SlopWorld mod/Tests bench/ipc/csharp bench/terminal-input/tests/http_transport; do (cd "$source" && dotnet csharpier check --log-level Warning .) || exit "$?"; done


# Validate a Release build with warnings as errors (requires game assemblies)
[group('mod')]
mod-check-build: (mod-build '' 'release' 'true')

# Validate a game-free Release build with pinned RimRef references
[group('mod')]
mod-check-build-rimref: (mod-build 'rimref' 'release' 'true')

# Run game-free C# tests
[positional-arguments]
[group('mod')]
mod-test *args: mod-restore
    dotnet run --project "mod/Tests/SlopWorld.Tests.csproj" --verbosity quiet --configuration Release -p:RestoreLockedMode=true -- --quiet "$@"

# Collect game-free C# coverage
[group('mod')]
mod-coverage: mod-restore mod-restore-tools
    mkdir -p "$COVERAGE_DIR"
    dotnet build mod/Tests/SlopWorld.Tests.csproj --verbosity quiet --nologo --configuration Release -p:Coverage=true -p:RestoreLockedMode=true
    dotnet tool run coverlet -- mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll --target dotnet --targetargs 'mod/Tests/bin/Release/net8.0/SlopWorld.Tests.dll --quiet' --include-test-assembly --exclude-by-file '**/mod/Tests/**/*.cs' --exclude-by-file '**/Client/Generated/Slopworld.cs' --format cobertura --output "$COVERAGE_DIR/csharp.cobertura.xml"

# Remove component build output
[group('mod')]
mod-clean:
    rm -f "mod/Assemblies/SlopWorld.dll"
    rm -rf mod/Source/SlopWorld/obj
