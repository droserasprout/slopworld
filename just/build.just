# Build

# Build the daemon and the launcher
[group('Build')]
daemon: api-contract
    cd slopd && {{ if VERSION != '' { 'SLOPWORLD_BUILD_VERSION="' + VERSION + '"' } else { '' } }} {{ CARGO }} build {{ CARGOFLAGS }}

# Build the mod against the game's assemblies
[group('Build')]
mod: api-contract validate-themes mod-deps stage-licenses
    version="{{ VERSION }}"; \
        if test -z "$version"; then version="$({{ PYTHON }} -m tools.version.mod_version)" || exit; fi; \
        {{ DOTNET }} build "{{ MOD_PROJECT }}" --configuration {{ if BUILD == 'release' { 'Release' } else { 'Debug' } }} \
            -p:RimWorldManaged="{{ if MANAGED =~ '^/' { MANAGED } else { justfile_directory() + '/' + MANAGED } }}" -p:InformationalVersion="$version" \
            -p:TreatWarningsAsErrors={{ MOD_WARNINGS_AS_ERRORS }} -p:RestoreLockedMode=true

# Restore locked mod runtime packages for Unity Mono
[group('Build')]
mod-deps:
    {{ DOTNET }} build mod/Dependencies/Runtime.csproj --configuration Release --verbosity quiet -p:RestoreLockedMode={{ MOD_DEPS_LOCKED }}

# Fetch and verify the latest RimWorld Harmony assembly
[group('Build')]
fetch-harmony destination='mod/Assemblies':
    {{ PYTHON }} -m tools.dependencies.fetch_harmony {{ quote(destination) }}

# Stage canonical license texts and attribution into the mod
[group('Build')]
stage-licenses:
    {{ PYTHON }} -m tools.licenses.stage_licenses

# Check staged license texts against the canonical sources
[group('Tests and coverage')]
check-licenses:
    {{ PYTHON }} -m tools.licenses.stage_licenses --check

# Validate the shipped UI and terminal theme catalogs
[group('Build')]
validate-themes:
    {{ PYTHON }} -m tools.themes.validate_themes

# Remove build output
[group('Build')]
clean:
    cd slopd && {{ CARGO }} clean
    rm -f "{{ MOD_DLL }}"
    rm -rf mod/Source/SlopWorld/obj
    rm -rf "{{ COVERAGE_DIR }}"

# Keep the old entry point for existing developer scripts.
[group('Build')]
[private]
protobuf-deps: mod-deps
