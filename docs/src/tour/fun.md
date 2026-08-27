# Fun

## The colony

Working agents build structures on the map — paving, graves, monuments, ancient buildings.
Finished work spreads the plague, a ground effect that ripens outward from each completed
site. Fire cannot spread into fully plagued cells, and plants do not regrow there.

Grandma mode replaces the plague with flower growth.

## Skyfallers

New agents arrive from the sky. The persona core drops as a skyfaller; pawns land in
drop pods with a short opening delay.

## Agent titles

The daemon can summarize agent prompts into short titles using an OpenRouter model. Codex
supports `never`, `once`, and `always` policies; Pi defaults to `always`. Host terminal
commands get a separate on/off toggle. Titles are cached in `prompt-summaries.toml` and
survive daemon restarts.

## Jukebox

The jukebox is a map building with one click target. No radio stations ship with SlopWorld —
users add their own as one-station TOML files under
`~/.local/share/slopworld/jukebox/`.

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

In sidecar mode (macOS), the same menu row toggles RimWorld's native music manager for
the bundled SlopWorld OST instead.

The jukebox menu also offers song recognition via `songrec`, a like button that appends
to `jukebox.toml`, and a history view.

## The baked background

The menu and loading screen use a cached, processed version of the game's own background
art. The processing runs through two presets:

- **rot** — darkens, drains, and tints the image toward plague violet; phases are
  independent draws.
- **glow** — lays a rainbow sheen and blinking constellation over the original; phases
  loop continuously.

Frames are cached as JPEG under `$XDG_CACHE_HOME/slopworld/bg/`. The depth axis
(how far into the effect) wanders on a mean-reverting random walk; the phase axis
(which redraw) advances on a timer.

## Eco mode backdrop

With eco mode on and no terminal open, the baked background covers the map as a dimmed,
slowly drifting quad. Agent pawns and the colony cat sway gently in front of it.

