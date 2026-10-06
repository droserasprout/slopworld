# Local releases require the game's assemblies; GitHub Actions only runs game-free checks.

# Build release archives, Arch/Debian packages, and checksums locally
[group('Release')]
release-package:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.latest package

# Rebuild locally and publish or update the rolling latest GitHub release
[group('Release')]
release-latest:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.latest publish

# Stage a source Arch package mod with the shared runtime allowlist and canonical notices
[group('Release')]
stage-arch-mod destination:
    {{ PYTHON }} -m tools.release.source_mod {{ quote(destination) }}

# Test release packaging and publication ordering without building or publishing
[group('Tests and coverage')]
test-release:
    {{ PYTHON }} -m pytest tools/release

# Build a Debian package locally (requires Debian/Ubuntu, dpkg-dev, and game assemblies)
[group('Release')]
pkg-debian:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.debian

# Prepare the Debian release compiler and packaging image
[group('Release')]
release-debian-image:
    {{ CONTAINER }} build {{ CONTAINER_BUILD_ARGS }} -t slopworld-debian-release -f packaging/debian/Dockerfile packaging/debian

# Test real Debian package archives in the prepared release compiler image
[group('Tests and coverage')]
test-release-debian:
    {{ CONTAINER }} run {{ CONTAINER_RUN_ARGS }} --rm --volume "{{ justfile_directory() }}:{{ justfile_directory() }}:ro" --workdir "{{ justfile_directory() }}" slopworld-debian-release python3 -m pytest tools/release/test_debian.py -q -p no:cacheprovider
