# Local releases require the game's assemblies; GitHub Actions only runs game-free checks.

# Build release binaries and mod, then package archives and checksums locally
[group('Release')]
release-package:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.latest package

# Rebuild locally and publish or update the rolling latest GitHub release
[group('Release')]
release-latest:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.release.latest publish

# Test release packaging and publication ordering without building or publishing
[group('Tests and coverage')]
test-release:
    {{ PYTHON }} -m pytest tools/release
