# Native app-bundle locations; MAC_GAME and MAC_MANAGED accept installation-specific overrides.
export MAC_RIMWORLD := env("MAC_RIMWORLD", env("HOME") + '/Documents/RimWorld.app')
export MAC_GAME := env("MAC_GAME", MAC_RIMWORLD + '/Contents/MacOS/RimWorld')
export MAC_RESOURCES := env("MAC_RESOURCES", MAC_RIMWORLD + '/Contents/Resources')
export MAC_MANAGED := env("MAC_MANAGED", MAC_RIMWORLD + '/Contents/MacOS')
export MAC_MODS := env("MAC_MODS", MAC_RIMWORLD + '/Mods')
export MAC_GAME_ARGS := env("MAC_GAME_ARGS", '')

