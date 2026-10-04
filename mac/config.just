# Native macOS RimWorld is an app bundle, so it needs its own references, mod destination and
# direct game executable. The GOG path is the useful default. Every value is overridable for a
# Steam or standalone install, or a friend whose home layout differs.
export MAC_RIMWORLD := env("MAC_RIMWORLD", env("HOME") + '/Documents/RimWorld.app')
export MAC_GAME := env("MAC_GAME", MAC_RIMWORLD + '/Contents/MacOS/RimWorld by Ludeon Studios')
export MAC_RESOURCES := env("MAC_RESOURCES", MAC_RIMWORLD + '/Contents/Resources')
export MAC_MANAGED := env("MAC_MANAGED", MAC_RESOURCES + '/Data/Managed')
export MAC_MODS := env("MAC_MODS", MAC_RIMWORLD + '/Mods')
export MAC_PROFILE := env("MAC_PROFILE", env("HOME") + '/Library/Application Support/SlopWorld/sidecar-profile')
export MAC_GAME_ARGS := env("MAC_GAME_ARGS", '')

