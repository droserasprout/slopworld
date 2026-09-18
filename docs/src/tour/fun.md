# Fun

## The colony

Working agents build structures on the map: paving, graves, monuments, ancient buildings.
Finished work spreads the plague, a ground effect that ripens outward from each completed
site. Fire cannot spread into fully plagued cells, and plants do not regrow there.

Grandma mode replaces the plague with flower growth.

## Skyfallers

New agents arrive from the sky. The persona core drops as a skyfaller; pawns land in
drop pods with a short opening delay.

## Jukebox

The jukebox is a map building with one click target. No radio stations ship with SlopWorld —
users add their own as one-station TOML files under
`~/.config/slopworld/jukebox/`.

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

The daemon decodes MP3 with Symphonia and feeds the host audio device. ICY metadata
becomes the track display. Volume multiplies RimWorld's existing audio settings. Muting
sends a null selection so unheard audio is not downloaded.

In sidecar mode, the same menu row toggles RimWorld's native music manager for the bundled
SlopWorld OST instead.

The jukebox menu also offers song recognition via `songrec`, a like button that appends
to `jukebox.toml`, and a history view.
