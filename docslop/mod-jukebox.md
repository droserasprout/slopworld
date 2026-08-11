# The jukebox

`Sim/Jukebox.cs` owns the building and menus; `Sim/Radio.cs` owns the selected source and
reports it to the daemon. Mute sends no source rather than setting volume to zero, so the
daemon does not download unheard audio. Selecting a source clears mute.

Stop-on-exit sends `source: null` during `Root.Shutdown`. `Radio.Quit` also latches the
shutdown state because `Application.Quit` lets later frames run; without the latch,
`Radio.Update` can restart playback. A killed process cannot send this message.

The station/preset key is stored in `SlopSettings.radio`; the daemon deliberately keeps no
selection. Unknown or removed keys fall back to the OST. Volume multiplies RimWorld's
existing audio sliders.

The Like action appends the normalized `artist - title` text, one line per action, to
`~/.local/share/slopworld/jukebox.toml` (or `$XDG_DATA_HOME/slopworld/jukebox.toml`). It is
machine music data, not mod settings.

## Daemon audio

The daemon decodes MP3 with Symphonia, resamples to the output device's format, and feeds a
bounded CPAL queue. The callback must never block. Station replacement increments a
generation so an old decoder cannot publish status or samples after a new selection.
Metadata is parsed from Icecast headers and in-stream ICY blocks and sent back in audio
status events.

Playback lives in the daemon because the game's Unity/FMOD path cannot reliably play these
streams: desktop AAC support is absent, cleartext HTTP is rejected by the player, FMOD's
streaming fetch lacks TLS, and an Icecast response has no `Content-Length`. A loopback
relay solves only the transport restrictions, not the unknown-length stream.

Tests must never contact radio stations. Only a user selecting a station in a real build
may open its stream; keep decoder and metadata coverage local and deterministic.

The hardcoded independent stations are Radio Paradise, WEFUNK, WALM, Kiosk Radio, WFMU,
dublab, SomaFM Secret Agent and Groove Salad, NTS Radio 1, and KEXP. Prefer direct HTTPS
MP3 streams with ICY metadata; redirects are acceptable when the station owns the stable URL.

## Assets

The OST files remain ordinary mod assets but are opened by the daemon. `SlopWorld_Bg1` is
still declared because the XML patch removing vanilla songs expects a `SlopWorld_` song;
remove both together.

The ground texture is `mod/Textures/SlopWorld/Jukebox.png`, generated with:

```sh
python3 tools/emoji.py --emoji 📻 --name Jukebox --size 128 --color --out mod/Textures/SlopWorld
```
