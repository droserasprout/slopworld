<a id="game-profiles"></a>

# Saves and profiles

## Profile selection

The launcher automatically creates a RimWorld save-data folder for each named peer:

```sh
slopworld connect tank http://tank:7717
slopworld --endpoint tank
```

`connect` checks the daemon, prompts for its key with hidden input when needed, saves
`<CONFIG>/endpoints/tank.toml`. It does not launch the game. To launch a saved peer, use `slopworld --endpoint NAME`. Running
`slopworld connect NAME` again verifies and refreshes its saved credentials.
`slopworld endpoints` lists saved names and URLs.
Keys are never displayed. A failed verification leaves the saved connection intact.

`local` is reserved for the daemon on this machine and cannot be registered with
`connect`. It is the default: run `slopworld` without `--endpoint` to launch it.

The selected peer uses `<DATA>/profiles/NAME`. `local` is the default.
`slopworld --endpoint NAME` selects another endpoint for one launch. Renaming a
peer changes its profile; changing its URL or key keeps the same profile. For an
explicit `SLOPD_ENDPOINT` file, the filename without `.toml` supplies the profile name.

`--sidecar` selects a separate catalog under `<CONFIG>/sidecar/`, with
profiles under `<DATA>/sidecar/profiles/NAME`. Use `slopworld --sidecar connect NAME`
and `slopworld --sidecar endpoints` to manage those peers. The [sidecar](../installation/sidecar.md)
and [macOS](../installation/macos.md) recipes select sidecar mode explicitly.

`--profile DIR` overrides automatic profile selection, followed by `SLOPWORLD_PROFILE`.
Explicit paths can share a folder. Profile paths cannot contain `=`.
See [Paths and files](../reference/paths.md) for locations and overrides.

New profiles start with master volume at 80%, game at 100%, music at 80%,
ambience at 0%, and UI at 30%. Existing preferences are preserved.

## Profile initialization and repair

Create the profile marker and missing mod list without opening the game:

```sh
slopworld --endpoint tank --init-profile
```

Existing settings and mod choices are preserved. With `--sidecar`, initialization
sets the sidecar UI default only when `Config/SlopWorld.toml` is missing.

## Mod list reset

To replace `Config/ModsConfig.xml`, add `--reset`:

```sh
slopworld --endpoint tank --init-profile --reset
```

This discards the existing mod list; it does not reset saves or the whole profile.

## Troubleshooting

Use the launcher for normal play. See [The mod disables itself at startup](../resources/troubleshooting.md#the-mod-refuses-to-patch)
for marker repair and [Multiple instances](../resources/troubleshooting.md#multiple-instances)
for launch conflicts.
