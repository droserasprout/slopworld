# Generated files

# Generate the locked Rust dependency license table
[group('Generated files')]
rust-licenses:
    {{ PYTHON }} -m tools.licenses.rust_licenses

# Generate stable shared protocol bindings
[group('Generated files')]
api-contract:
    {{ PYTHON }} -m tools.protocol.api_contract

# Generate the mdBook API route inventory
[group('Generated files')]
api-docs: api-contract
    {{ PYTHON }} -m tools.docs.api_docs

# Generate the environment/API/CLI reference
[group('Generated files')]
reference:
    {{ PYTHON }} -m tools.docs.reference

# Bake the loading screen font atlas from the bundled source font
[group('Generated files')]
bake-loading-font:
    {{ UV }} run --locked --extra assets python -m tools.assets.loading_font_atlas --font "{{ FONT_SOURCE }}"

# Reject uncommitted generated protocol changes
[group('Generated files')]
check-generated: api-contract
    git diff --exit-code -- shared slopd/src/shared mod/Source/SlopWorld/Client/Generated

# Bake the shared UI icons from an installed Nerd Font
[group('Generated files')]
bake-icons:
    {{ UV }} run --locked --extra assets python -m tools.assets.icons

# Resolve and update the Python dependency lockfile
[group('Generated files')]
lock-tools:
    {{ UV }} lock

# Synchronize the Python tooling environment from the lockfile
[group('Build')]
sync-tools:
    {{ UV }} sync --locked
