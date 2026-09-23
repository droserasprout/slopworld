# Fun

## The colony

Working agents build structures on the map: paving, graves, monuments, ancient buildings.
Finished work spreads the plague, a ground effect that spreads outward from each completed site. Fire cannot spread into fully plagued cells, and plants do not regrow there.

Gentle mode replaces plague damage with flowers.

## Skyfallers

New agents arrive from the sky. The persona core drops as a skyfaller.
Pawns land in drop pods with a short opening delay.

## Jukebox

The jukebox is a map building with one click target. It always includes the SlopWorld original soundtrack (OST).
On a native Linux daemon, the jukebox also supports Spotify when `ncspot` is installed.
Add a radio station from **Settings > Audio > Add source**.
You can also manage their TOML files under `~/.config/slopworld/jukebox/`. Each file defines one station.

The **Sources** table controls which entries appear in the jukebox. Disable Spotify in
Settings to hide it. The mod also hides Spotify when `ncspot` is unavailable on the daemon host.

```toml
id = "example"
default_rate = 128

[metadata]
name = "Example Radio"
title_regex = '^\s*(?<title>.+?)\s+by\s+(?<artist>.+?)\s*$'

[[stream]]
rate = 128
key = "example-128"
url = "https://stream.example.org/example-128"
```

The daemon decodes MP3 with Symphonia and sends audio to the host audio device. ICY metadata
becomes the track display. Volume multiplies RimWorld's existing audio settings. Muting sends a null selection so the daemon does not download audio that you cannot hear.

In sidecar mode, the same menu row toggles RimWorld's native music manager for the bundled
SlopWorld OST instead.

The jukebox menu also offers song recognition via `songrec`, a like button that appends
to `jukebox.toml`, and a history view.

### Spotify proof of concept

You need a Spotify Premium account. On a native Linux daemon:

1. Install `ncspot`.
2. Choose **Play → Spotify (ncspot)** in the jukebox menu.
3. Complete ncspot's login.
4. Choose music in its terminal.

**Open Spotify player** returns to that terminal.
Hiding the terminal leaves playback running. The jukebox displays the track reported by ncspot.

This version closes ncspot when muted or when switching to OST/radio. Unmuting starts the player again.
Use its terminal to resume or choose music. The existing
**Stop on exit** setting also applies. Playback controls and Spotify library changes stay inside ncspot.
The jukebox Like action still writes only SlopWorld's likes.

The daemon needs `XDG_RUNTIME_DIR` and host audio access. The player uses a private
runtime directory beneath it, preserving `PULSE_SERVER` or using the host's usual
PulseAudio/PipeWire socket. ncspot retains ownership of its normal configuration and
login credentials. This proof of concept does not support sidecar playback.
