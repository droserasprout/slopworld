# GOG game installation

# Log into GOG interactively and save the gogdl token
[group('GOG game installation')]
gogdl-login:
    bash tools/gogdl.sh login

# Install RimWorld's native Linux build with gogdl
[group('GOG game installation')]
gogdl-install:
    bash tools/gogdl.sh install

# Update the local RimWorld copy with gogdl
[group('GOG game installation')]
gogdl-update:
    bash tools/gogdl.sh update
