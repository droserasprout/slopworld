# Local releases require the game's assemblies; GitHub Actions only runs game-free checks.

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

# Build a Fedora RPM (requires rpm-build and game assemblies)
[group('Release')]
pkg-fedora:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.fedora

# Build the Fedora release image
[group('Release')]
release-fedora-image:
    {{ CONTAINER }} build {{ CONTAINER_BUILD_ARGS }} -t slopworld-fedora-release -f packaging/fedora/Dockerfile packaging/fedora

# Test RPM archives in the prepared Fedora image
[group('Tests and coverage')]
test-release-fedora:
    {{ CONTAINER }} run {{ CONTAINER_RUN_ARGS }} --rm --security-opt label=disable --volume "{{ justfile_directory() }}:{{ justfile_directory() }}:ro" --workdir "{{ justfile_directory() }}" slopworld-fedora-release python3 -m pytest tools/release/test_fedora.py -q -p no:cacheprovider
