# The jukebox

`Defs/Jukebox.xml` defines `SlopJukebox`: a non-selectable, non-edifice building
with no hit points, zero flammability and `Standable` passability — scenery with one
click target. `Sim/Jukebox.cs` owns the building and menus; `Sim/Radio.cs` owns
selection and reports it to `slopd`. The daemon owns the station catalog. Shipped stations are
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
the future donation action. A user file may omit `id`; its filename stem becomes the station
key. HTTP response breaks are reconnected below the decoder, preserving decoder state and queued
audio; a decoder is rebuilt only if decoding itself ends.

Daemon-backed mute sends `selection: null` so unheard audio is not downloaded. In sidecar mode, where
the daemon cannot play audio, the same menu row toggles the native game's music manager for the
SlopWorld OST. Vanilla SongDefs are stripped in both modes.
Stop-on-exit sends the same during shutdown; `Radio.Quit` latches because Unity may run frames
after `Application.Quit`. A killed process cannot send it. Volume multiplies RimWorld's existing
audio settings.

## Daemon audio

**Testing is local-only. Never request a real station or any other internet service.** Use
deterministic fixtures or a loopback server for decoder, ICY, buffering, and reconnect tests.

The daemon decodes MP3 with Symphonia, resamples to the device format and feeds a bounded
CPAL queue; live playback builds one second of decoded headroom at startup and again after an
underrun, and the callback never blocks. A selection generation prevents an old decoder from
publishing after replacement. ICY headers and in-stream metadata become audio status events.
Tests use local deterministic fixtures and loopback only, never stations.

Live ureq connections have a connect timeout but no response/body/global timeout. Ureq 3.3 carries
its response-header deadline into body reads, so setting that nominally header-only timeout caused
an artificial disconnect every fifteen seconds.

The daemon is required because Unity/FMOD cannot reliably handle the target HTTPS,
AAC, Icecast and unknown-length streams. Shipped sources include Radio Paradise,
WEFUNK, WALM, Kiosk Radio, WFMU, dublab, NTS and KEXP; prefer direct HTTPS
MP3 streams with ICY metadata.

## Local audio and likes

The dated OST remains in `mod/Sounds/SlopWorld/OST/` as 192 kbps OGG, but the daemon
opens it as a directory and plays a non-repeating shuffled bag. `Songs.xml` keeps the
matching `SlopWorld_` defs, which nothing plays while `Radio` holds the music manager
disabled in daemon mode; sidecar mode enables the native manager for those same SlopWorld defs.
RimWorld's original music defs are stripped in both modes. What moving the tracks out would save
is [startup-time](startup-time.md).
`split_ost.py` stages exports; `install_ost.py` installs them and updates the catalog.

Like appends a `[[like]]` table with `at`, `source`, effective `artist`/`title`, and raw
`original_artist`/`original_title` fields to `$XDG_DATA_HOME/slopworld/jukebox.toml`.
Because ICY supplies one raw title string, `original_artist` is empty until a source supplies
structured artist metadata; the raw station string is always retained in `original_title`.
Recognition lives in `SongRecognizer` (Sim/), a Unity-free service with an injected process
runner so its device pick, JSON, timeout and cancellation are unit-tested with a fake. It runs
`songrec` with a 30-second timeout, selecting the current PipeWire/Pulse default sink's
`.monitor` source when `pactl` names one and falling back to SongRec's default input otherwise.
`Radio` runs it off the Unity thread, refuses a second concurrent lookup, and applies the result
only when the source and track generation still match. The UI (Jukebox menu, palette, audio
page) shows a transient recognizing state that names the input and can be cancelled or retried,
and presents the recognized pair beside the station's own line rather than replacing it.
`History` opens a maximized, newest-first table with search, per-field copy, a detail panel for
long original metadata, local-time stamps and empty/error states; its file reader is
`JukeboxHistory` (UI/), which reads the current structured `[[like]]` tables apart from
rendering. Storage settings has a button to open the likes file in the configured editor. The
map texture is generated with `tools/emoji.py`; it is unrelated to station configuration.
