# The jukebox

`Sim/Jukebox.cs` owns the building and menus; `Sim/Radio.cs` owns selection and
reports it to `slopd`. The daemon owns the station catalog. Shipped stations are
compiled from `slopd/jukebox/`; user files are one-station TOMLs under
`$XDG_CONFIG_HOME/slopworld/jukebox/` (`SLOPD_JUKEBOX` overrides). A matching `id`
replaces a shipped station; new ids append in filename order.

```toml
id = "example"
default_rate = 128

[metadata]
name = "Example Radio"
donate = "https://example.org/support"
title_regex = '^\s*(?<title>.+?)\s+by\s+(?<artist>.+?)\s*$'

[[stream]]
rate = 128
key = "example-128"
url = "https://stream.example.org/example-128"
```

The wire catalog calls the array `streams`; the mod drops stations with no matching
stream. Stable station/stream keys are saved in `SlopSettings.radio`; URLs stay in
the daemon. Catalogs arrive as the root `jukebox`
WebSocket event and through `GET /api/jukebox`; the mod retains the last catalog while
reconnecting. `metadata.title_regex` normalizes ICY titles; `donate` is retained for
the future donation action.

Mute sends `selection: null` so unheard audio is not downloaded. Stop-on-exit sends the
same during shutdown; `Radio.Quit` latches because Unity may run frames after
`Application.Quit`. A killed process cannot send it. Volume multiplies RimWorld's
existing audio settings.

## Daemon audio

The daemon decodes MP3 with Symphonia, resamples to the device format and feeds a
bounded CPAL queue; the callback never blocks. A selection generation prevents an old
decoder from publishing after replacement. ICY headers and in-stream metadata become
audio status events. Tests use local deterministic fixtures and never contact stations.

The daemon is required because Unity/FMOD cannot reliably handle the target HTTPS,
AAC, Icecast and unknown-length streams. Shipped sources include Radio Paradise,
WEFUNK, WALM, Kiosk Radio, WFMU, dublab, SomaFM, NTS and KEXP; prefer direct HTTPS
MP3 streams with ICY metadata.

## Local audio and likes

The dated OST remains in `mod/Sounds/SlopWorld/OST/` as 192 kbps OGG, but the daemon
opens it as a directory and plays a non-repeating shuffled bag. `Songs.xml` keeps the
matching `SlopWorld_` defs, which nothing plays while `Radio` holds the music manager
disabled; what moving the tracks out would save is [startup-time](startup-time.md).
`split_ost.py` stages exports; `install_ost.py` installs them and updates the catalog.

Like appends `ISO-8601 UTC timestamp<TAB>artist - title` lines to
`$XDG_DATA_HOME/slopworld/jukebox.toml`; Storage settings has a button to open it in the
configured editor. The map texture is generated with
`tools/emoji.py`; it is unrelated to station configuration.
