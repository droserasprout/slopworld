# Regenerate all outputs from reviewed inputs; stage notices last
[group('generate')]
generate-all: generate-protocol generate-api-docs generate-loading-font generate-icons generate-emojis generate-emoji-atlas generate-fileicons generate-appicon generate-favicon generate-daemon-licenses generate-sidecar-licenses generate-licenses

# Generate protocol
[group('generate')]
generate-protocol:
    uv run --locked python -m tools.protocol.api_contract

# Generate api docs
[group('generate')]
generate-api-docs:
    uv run --locked python -m tools.docs.api_docs

# Generate loading font
[group('generate')]
generate-loading-font font='assets/fonts/clacon2.ttf':
    uv run --locked --extra assets python -m tools.assets.loading_font_atlas --font {{ quote(font) }}

# Generate icons
[group('generate')]
generate-icons:
    uv run --locked --extra assets python -m tools.assets.icons

# Generate appicon
[group('generate')]
generate-appicon:
    uv run --locked --extra assets python -m tools.assets.appicon

# Generate favicon
[group('generate')]
generate-favicon:
    uv run --locked --extra assets python -m tools.assets.favicon

# Generate daemon licenses
[group('generate')]
generate-daemon-licenses:
    uv run --locked python -m tools.licenses.rust_licenses

# Generate sidecar licenses
[group('generate')]
generate-sidecar-licenses:
    uv run --locked python -m tools.licenses.sidecar_licenses

# Generate licenses
[group('generate')]
generate-licenses:
    uv run --locked python -m tools.licenses.stage_licenses

# Check generated protocol outputs without changing the checkout
[group('generate')]
generate-check-protocol:
    uv run --locked python -m tools.protocol.api_contract --check

# Check staged notices against canonical licenses
[group('generate')]
generate-check-licenses:
    uv run --locked python -m tools.licenses.stage_licenses --check

# Generate standalone emoji originals and silhouettes
[group('generate')]
generate-emojis:
    uv run --locked --extra assets python -m tools.assets.emoji

# Generate text emoji artwork and slot metadata together
[group('generate')]
generate-emoji-atlas:
    uv run --locked --extra assets python -m tools.assets.emoji_atlas

# Generate file icons from vendored SVGs
[group('generate')]
generate-fileicons:
    uv run --locked --extra assets python -m tools.assets.fileicons
