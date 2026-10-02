# Game profiles

## Choose a profile

The launcher uses a separate RimWorld save-data folder. A normal launch initializes
missing profile data automatically:

```sh
slopworld --profile /path/to/profile
```

See [Paths and files](../reference/paths.md) for defaults and overrides. Standard
native, [sidecar](sidecar.md), and [macOS](macos.md) Make workflows use separate
paths. Explicit paths can select the same folder, so choose separate folders when
you want separate saves. Profile paths cannot contain `=`.

## Initialize or repair without launching

Create the profile marker and missing mod list without opening the game:

```sh
slopworld --profile /path/to/profile --init-profile
```

Existing settings and mod choices are preserved. With `--sidecar`, initialization
sets the sidecar UI default only when `Config/SlopWorld.toml` is missing.

## Replace the mod list

To replace `Config/ModsConfig.xml`, add `--reset`:

```sh
slopworld --profile /path/to/profile --init-profile --reset
```

This discards the existing mod list; it does not reset saves or the whole profile.

## Troubleshooting

Use the launcher for normal play. See [The mod refuses to patch](../reference/troubleshooting.md#the-mod-refuses-to-patch)
for marker repair and [Multiple instances](../reference/troubleshooting.md#multiple-instances)
for launch conflicts.
