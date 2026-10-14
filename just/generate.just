set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Regenerate all outputs from reviewed inputs; stage notices last
all: protocol api-docs loading-font icons appicon favicon daemon-licenses sidecar-licenses licenses

# Generate protocol
protocol:
    ${PYTHON} -m tools.protocol.api_contract

# Generate api docs
api-docs:
    ${PYTHON} -m tools.docs.api_docs

# Generate loading font
loading-font:
    ${UV} run --locked --extra assets python -m tools.assets.loading_font_atlas --font "${FONT_SOURCE}"

# Generate icons
icons:
    ${UV} run --locked --extra assets python -m tools.assets.icons

# Generate appicon
appicon:
    ${UV} run --locked --extra assets python -m tools.assets.appicon

# Generate favicon
favicon:
    ${UV} run --locked --extra assets python -m tools.assets.favicon

# Generate daemon licenses
daemon-licenses:
    ${PYTHON} -m tools.licenses.rust_licenses

# Generate sidecar licenses
sidecar-licenses:
    ${PYTHON} -m tools.licenses.sidecar_licenses

# Generate licenses
licenses:
    ${PYTHON} -m tools.licenses.stage_licenses

# Check generated protocol outputs without changing the checkout
check-protocol:
    ${PYTHON} -m tools.protocol.api_contract --check

# Check staged notices against canonical licenses
check-licenses:
    ${PYTHON} -m tools.licenses.stage_licenses --check
