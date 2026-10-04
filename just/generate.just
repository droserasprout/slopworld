# Generated files

# Generate stable shared protocol bindings
[group('Generated files')]
api-contract:
    mkdir -p mod/Source/SlopWorld/Client/Generated
    protoc -I shared --csharp_out=mod/Source/SlopWorld/Client/Generated shared/slopworld.proto
    {{ PYTHON }} tools/wire_contract.py
    {{ PYTHON }} tools/protobuf_http.py

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
    {{ PYTHON }} tools/loading_font_atlas.py --font "{{ FONT_SOURCE }}"

# Reject uncommitted generated protocol changes
[group('Generated files')]
check-generated: api-contract
    git diff --exit-code -- shared slopd/src/shared mod/Source/SlopWorld/Client/Generated

# Bake the shared UI icons from an installed Nerd Font
[group('Generated files')]
bake-icons:
    {{ PYTHON }} tools/icons.py
