# Log into GOG interactively
[group('game')]
game-login:
    uv run --locked python -m tools.host.gogdl login

# Install RimWorld through GOG
[group('game')]
game-install:
    uv run --locked python -m tools.host.gogdl install

# Update RimWorld through GOG
[group('game')]
game-update:
    uv run --locked python -m tools.host.gogdl update
