# Game profiles

The launcher uses a separate RimWorld save-data folder with a `slopworld.profile`
marker. Select it with `slopworld --profile /path/to/profile`. Default locations and
environment overrides are in [Paths and files](../reference/paths.md). Native and
sidecar defaults differ; an explicit path can select the same folder, so choose
separate paths when you want separate saves.

To create missing profile files or restore the marker without starting the game:

```sh
slopworld --profile /path/to/profile --init-profile
```

Existing profile choices are preserved. To explicitly replace the profile's mod
list, add `--reset`:

```sh
slopworld --profile /path/to/profile --init-profile --reset
```

Reset discards the existing mod list. `--sidecar` selects the sidecar UI default
during initialization. `--print` prints launch arguments without seeding or starting
the game and cannot be combined with `--init-profile`. Profile paths cannot contain
`=`. The launcher excludes simultaneous launches of the same profile.

Use the launcher for normal play. If the mod refuses to patch, see
[troubleshooting](../reference/troubleshooting.md#the-mod-refuses-to-patch).
Platform setup belongs to [macOS](macos.md) and [sidecar](sidecar.md).
