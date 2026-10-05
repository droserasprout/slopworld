# Generated files

# Refresh generated files after source changes, then stage distribution notices
[group('Generated files')]
refresh: refresh-protocol refresh-api-docs refresh-reference refresh-loading-font refresh-icons refresh-rust-licenses refresh-licenses

# Generate the locked Rust dependency license table
[group('Generated files')]
refresh-rust-licenses:
    {{ PYTHON }} -m tools.licenses.rust_licenses

# Generate stable shared protocol bindings
[group('Generated files')]
refresh-protocol:
    {{ PYTHON }} -m tools.protocol.api_contract

# Generate the mdBook API route inventory
[group('Generated files')]
refresh-api-docs:
    {{ PYTHON }} -m tools.docs.api_docs

# Generate the environment/API/CLI reference
[group('Generated files')]
refresh-reference:
    {{ PYTHON }} -m tools.docs.reference

# Bake the loading screen font atlas from the bundled source font
[group('Generated files')]
refresh-loading-font:
    {{ UV }} run --locked --extra assets python -m tools.assets.loading_font_atlas --font "{{ FONT_SOURCE }}"

# Check protocol outputs without changing the checkout
[group('Generated files')]
check-generated:
    {{ PYTHON }} -m tools.protocol.api_contract --check

# Bake the shared UI icons from the bundled Nerd Font
[group('Generated files')]
refresh-icons:
    {{ UV }} run --locked --extra assets python -m tools.assets.icons

# Resolve and update the Python dependency lockfile
[group('Generated files')]
lock-tools:
    {{ UV }} lock

# Synchronize the Python tooling environment from the lockfile
[group('Build')]
sync-tools:
    {{ UV }} sync --locked

# Stage canonical license texts and attribution into the mod
[group('Generated files')]
refresh-licenses:
    {{ PYTHON }} -m tools.licenses.stage_licenses


# Compatibility entry point; use refresh-rust-licenses.
[private]
rust-licenses: refresh-rust-licenses

# Compatibility entry point; use refresh-protocol.
[private]
api-contract: refresh-protocol

# Compatibility entry point; use refresh-api-docs.
[private]
api-docs: refresh-api-docs

# Compatibility entry point; use refresh-reference.
[private]
reference: refresh-reference

# Compatibility entry point; use refresh-loading-font.
[private]
bake-loading-font: refresh-loading-font

# Compatibility entry point; use refresh-icons.
[private]
bake-icons: refresh-icons

# Compatibility entry point; use refresh-licenses.
[private]
stage-licenses: refresh-licenses
