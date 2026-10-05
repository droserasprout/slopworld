# GOG game installation

# Log into GOG interactively and save the gogdl token
[group('GOG game installation')]
gogdl-login:
    {{ PYTHON }} -m tools.host.gogdl login

# Install RimWorld's native Linux build with gogdl
[group('GOG game installation')]
gogdl-install:
    {{ PYTHON }} -m tools.host.gogdl install

# Update the local RimWorld copy with gogdl
[group('GOG game installation')]
gogdl-update:
    {{ PYTHON }} -m tools.host.gogdl update
