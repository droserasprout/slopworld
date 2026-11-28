# Install or update RimWorld's unstable Steam branch; prompts for login as needed
[group('game')]
game-steam-update username:
    "$STEAMCMD" +force_install_dir "$RIMWORLD" +login {{quote(username)}} +app_update 294100 -beta unstable validate +quit

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
