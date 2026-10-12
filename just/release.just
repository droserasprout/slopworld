# Local releases require the game's assemblies; GitHub Actions only runs game-free checks.

# Update product version files for a dated, human-maintained changelog release
[group('Release')]
bump-version version:
    {{ PYTHON }} -m tools.version.bump {{ quote(version) }}

# Validate tagged main, refresh, lint, test, and build release packages locally
[group('Release')]
release-package:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.publish package

# Validate tagged main, refresh, lint, test, build, and publish a versioned release
[group('Release')]
release-publish:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.publish publish

# Stage a source Arch package mod with the shared runtime allowlist and canonical notices
[group('Release')]
stage-arch-mod destination:
    {{ PYTHON }} -m tools.release.source_mod {{ quote(destination) }}

# Test release packaging and publication ordering without building or publishing
[group('Tests and coverage')]
test-release:
    {{ PYTHON }} -m pytest tools/release

# Build a native package (arch, debian, or fedora; requires packaging tools and game assemblies)
[group('Release')]
pkg-native distribution:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.native {{ quote(distribution) }}

# Prepare a release compiler image (arch, debian, or fedora)
[group('Release')]
release-image distribution:
    {{ CONTAINER }} build {{ CONTAINER_BUILD_ARGS }} -t "slopworld-{{ distribution }}-release" -f "packaging/{{ distribution }}/Dockerfile" "packaging/{{ distribution }}"

# Test real native archives in a prepared release image (arch, debian, or fedora)
[group('Tests and coverage')]
test-release-native distribution:
    {{ CONTAINER }} run {{ CONTAINER_RUN_ARGS }} {{ if distribution == "arch" { "--privileged" } else { "" } }} --rm --security-opt label=disable --volume "{{ justfile_directory() }}:{{ justfile_directory() }}:ro" --workdir "{{ justfile_directory() }}" "slopworld-{{ distribution }}-release" python3 -m pytest "tools/release/test_{{ distribution }}.py" -q -p no:cacheprovider
