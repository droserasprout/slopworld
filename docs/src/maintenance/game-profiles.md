<a id="game-profiles"></a>

# Saves and profiles

## Profile selection

The launcher uses a separate RimWorld save-data folder. A normal launch initializes
missing profile data automatically:

```sh
slopworld --profile /path/to/profile
```

New profiles start with master volume at 80%, game at 100%, music at 80%,
ambience at 0%, and UI at 30%. Existing preferences are preserved.

See [Paths and files](../reference/paths.md) for defaults and overrides. Native mode defaults to `<DATA>/profile`; `--sidecar` defaults to
`<DATA>/sidecar/profile`. The [sidecar](../installation/sidecar.md) and
[macOS](../installation/macos.md) recipes select sidecar mode explicitly. Explicit paths can select the same folder, so choose separate folders when
you want separate saves. Profile paths cannot contain `=`.

## Profile initialization and repair

Create the profile marker and missing mod list without opening the game:

```sh
slopworld --profile /path/to/profile --init-profile
```

Existing settings and mod choices are preserved. With `--sidecar`, initialization
sets the sidecar UI default only when `Config/SlopWorld.toml` is missing.

## Mod list reset

To replace `Config/ModsConfig.xml`, add `--reset`:

```sh
slopworld --profile /path/to/profile --init-profile --reset
```

This discards the existing mod list; it does not reset saves or the whole profile.

## Troubleshooting

Use the launcher for normal play. See [The mod disables itself at startup](../resources/troubleshooting.md#the-mod-refuses-to-patch)
for marker repair and [Multiple instances](../resources/troubleshooting.md#multiple-instances)
for launch conflicts.
