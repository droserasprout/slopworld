# Build the book and validate navigation, links and anchors
[group('docs')]
docs-build:
    cd docs && mdbook build
    uv run --locked python -m tools.docs.check_book

# Serve the book
[group('docs')]
docs-serve:
    cd docs && mdbook serve

# Validate book navigation, links and anchors
[group('docs')]
docs-check:
    uv run --locked python -m tools.docs.check_book

# Convert book PNGs to JPEG copies, preserving originals
[group('docs')]
docs-images quality='15':
    uv run --locked python -m tools.docs.compress_images --quality {{ quote(quality) }}
