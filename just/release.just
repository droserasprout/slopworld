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

# Resolve and validate a hosted release or non-publishing preview
[arg('mode', pattern='^(release|preview)$')]
[group('release')]
release-ci-prepare mode='release':
    uv run --locked python -m tools.release.ci prepare {{ if mode == 'preview' { '--preview' } else { '' } }}

# Build release artifacts after the workflow's tests pass
[arg('mode', pattern='^(release|preview)$')]
[group('release')]
release-ci-package mode='release':
    uv run --locked python -m tools.release.ci package {{ if mode == 'preview' { '--preview' } else { '' } }}

# Verify downloaded checksums and identity without compiling
[arg('mode', pattern='^(release|preview)$')]
[group('release')]
release-ci-verify mode='release':
    uv run --locked python -m tools.release.ci verify {{ if mode == 'preview' { '--preview' } else { '' } }}

# Verify attestations and publish the exact hosted build, without rebuilding
[group('release')]
release-ci-publish:
    uv run --locked python -m tools.release.ci publish
