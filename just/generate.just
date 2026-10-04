# Generated files

# Generate the locked Rust dependency license table
[group('Generated files')]
rust-licenses:
    {{ PYTHON }} tools/rust_licenses.py

# Generate stable shared protocol bindings
[group('Generated files')]
api-contract:
    {{ PYTHON }} tools/api_contract.py

# Generate the mdBook API route inventory
[group('Generated files')]
api-docs: api-contract
    {{ PYTHON }} tools/api_docs.py

# Generate the environment/API/CLI reference
[group('Generated files')]
reference:
    {{ PYTHON }} tools/reference.py

# Bake the loading screen font atlas from the bundled source font
[group('Generated files')]
bake-loading-font:
    {{ PYTHON }} tools/assets/loading_font_atlas.py --font "{{ FONT_SOURCE }}"

# Reject uncommitted generated protocol changes
[group('Generated files')]
check-generated: api-contract
    git diff --exit-code -- shared slopd/src/shared mod/Source/SlopWorld/Client/Generated

# Bake the shared UI icons from an installed Nerd Font
[group('Generated files')]
bake-icons:
    {{ PYTHON }} tools/assets/icons.py
