set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Log into GOG interactively
login:
    ${PYTHON} -m tools.host.gogdl login

# Install RimWorld through GOG
install:
    ${PYTHON} -m tools.host.gogdl install

# Update RimWorld through GOG
update:
    ${PYTHON} -m tools.host.gogdl update
