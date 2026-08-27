# Known limitations

## Private networking across a network change

Private agents run through a long-lived `pasta` network namespace. An established TCP
connection can remain stuck when the host changes uplinks (Wi-Fi to Ethernet, VPN
toggle), even though DNS and new connections work. This is a stale connection in the
network path, not agent state corruption.

After changing networks, use the agent's restart action. Restart recreates `pasta` and
the agent process while preserving private state. Do not use Reset Storage for this;
reset is for damaged or intentionally fresh tool state.

Automatic recovery is not attempted. Deciding that an agent is idle can kill a local
command, and resuming or restoring input can duplicate a request that completed remotely
while its response was lost.

## No seccomp or disk quota

The sandbox does not apply a seccomp filter, `--new-session`, or per-agent disk quota.
Resource caps (memory, PIDs, open files, CPU) are off unless configured in the project
or agent limits.

## Project directories are read-write

Project directories, including `.git`, are mounted read-write by design. Agents can
install hooks or alter git configuration inside the project. Back up your repositories
before granting agent access.

## RimWorld 1.6 only

The mod patches tick methods and UI targets specific to RimWorld 1.6. Earlier versions
use different signatures and will fail at runtime. Harmony patch failures appear in
`Player.log` as `patching incomplete:`.

## No save migration

Saves and configuration created with removed defs are not migrated. Start a new planet
if the mod's defs have changed since the save was created.

## Debug preset exposure

The `slopworld-debug` preset is an intentionally broad host escape for game development.
It mounts the game install, profile, tmux socket, `/proc`, `/sys`, X11/Wayland devices,
and several development caches read-write. Its `escapes` warning is functional, not
cosmetic.

## Font rendering

`GameFont.Tiny` may render as Small in some configurations. The mod measures through
its own `SlopWidgets.LineHOf` and `TinyH` to avoid layout drift. Missing glyphs still
advance a line; the mod tests with `Font.HasCharacter` and substitutes before drawing.
