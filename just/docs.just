set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Build the book and validate navigation, links and anchors
build:
    cd docs && mdbook build
    $PYTHON -m tools.docs.check_book

# Serve the book
serve:
    cd docs && mdbook serve

# Validate book navigation, links and anchors
check:
    $PYTHON -m tools.docs.check_book

# Convert book PNGs to JPEG copies, preserving originals
images quality='15':
    $PYTHON -m tools.docs.compress_images --quality {{ quote(quality) }}
