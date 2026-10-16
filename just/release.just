# Update product versions for a dated changelog release
[group('release')]
release-bump-version version:
    uv run --locked python -m tools.version.bump {{ quote(version) }}

# Validate, build and publish a versioned release
[group('release')]
release-publish:
    uv run --locked python -m tools.release.publish publish

# Validate tagged main, regenerate, check, test and package locally
[group('release')]
release-package:
    uv run --locked python -m tools.release.publish package
