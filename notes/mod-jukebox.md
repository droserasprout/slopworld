# The jukebox

`Defs/Jukebox.xml` defines `SlopJukebox`: a non-selectable, non-edifice building
with no hit points, zero flammability and `Standable` passability — scenery with one
click target. `Sim/Jukebox/Jukebox.cs` owns the building and menus; `Sim/Jukebox/Radio.cs` owns
selection and reports it to `slopd`. The daemon owns the station catalog. There are no
shipped radio stations: user files are one-station TOMLs under
`$XDG_DATA_HOME/slopworld/jukebox/` (`SLOPD_JUKEBOX` overrides). A matching `id`
replaces the existing user entry; new ids append in filename order.

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
stream. Stable station/stream keys are saved in `ModSettings.radio`; URLs stay in
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
Top-bar and map-cell now-playing tooltips use a cache refreshed at the radio update cadence or
when the track version changes, avoiding repeated native music-manager queries in sidecar mode.

## Daemon audio

**Testing is local-only. Never request a real station or any other internet service.** Use
deterministic fixtures or a loopback server for decoder, ICY, buffering, and reconnect tests.

The daemon decodes MP3 with Symphonia, resamples to the device format and feeds a bounded
CPAL queue; live playback builds one second of decoded headroom at startup and again after an
underrun, and the callback never blocks. A selection generation prevents a stale decoder from
publishing after replacement. ICY headers and in-stream metadata become audio status events.
Tests use local deterministic fixtures and loopback only, never stations.

Live ureq connections have a connect timeout but no response/body/global timeout. Ureq 3.3 carries
its response-header deadline into body reads, so setting that nominally header-only timeout caused
an artificial disconnect every fifteen seconds.

The daemon is required because Unity/FMOD cannot reliably handle the target HTTPS,
AAC, Icecast and unknown-length streams. User-provided sources should prefer direct
HTTPS MP3 streams with ICY metadata.

Local OST playback, likes, recognition, and history are covered in
[jukebox local audio and history](mod-jukebox-library.md).
