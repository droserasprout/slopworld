# Generated files

# Refresh generated files after source changes, then stage distribution notices
[group('Generated files')]
refresh: refresh-protocol refresh-introduction refresh-api-docs refresh-loading-font refresh-icons refresh-appicon refresh-favicon refresh-daemon-licenses refresh-licenses

# Generate the book introduction from README
[group('Generated files')]
refresh-introduction:
    {{ PYTHON }} -m tools.docs.introduction

# Generate the locked Rust dependency license table
[group('Generated files')]
refresh-daemon-licenses:
    {{ PYTHON }} -m tools.licenses.rust_licenses

# Generate stable shared protocol bindings
[group('Generated files')]
refresh-protocol:
    {{ PYTHON }} -m tools.protocol.api_contract

# Generate the mdBook API route inventory
[group('Generated files')]
refresh-api-docs:
    {{ PYTHON }} -m tools.docs.api_docs

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

# Bake the desktop application icon from the robot face and bundled rose emoji
[group('Generated files')]
refresh-appicon:
    {{ UV }} run --locked --extra assets python -m tools.assets.appicon

# Fit the shared robot and rose artwork to the documentation favicon
[group('Generated files')]
refresh-favicon:
    {{ UV }} run --locked --extra assets python -m tools.assets.favicon

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
