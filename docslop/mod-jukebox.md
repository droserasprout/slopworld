# The jukebox

A radio set rides down in the pod with the first clanker. LMB on it opens a menu
of two stations - "OST" and "RadioParadise Main" - and the station's row opens a
second menu of the quality presets it serves. Whatever is playing is marked.
`Sim/Jukebox.cs` is the box, `Sim/Radio.cs` is the sound.

A `FloatMenuOption` holds no children, so the nested list is a second `FloatMenu`
opened from the first one's action. That is safe because `FloatMenuOption.Chosen`
calls `PreOptionChosen` - which closes the parent - before it invokes the action.

- The def is `SlopJukebox`: no hit points, no flammability, not an edifice and
  not selectable. Nothing builds it, breaks it or blocks on it.
- `AgentColony.Spawn` adds one to the pod's cargo, behind a saved `_jukeboxSent`
  latch as well as `Jukebox.On(map)`. The latch is the part that matters: a
  reconcile sends a whole colony's worth of pods in one tick, and a thing inside
  a pod in the air is not on the map, so "is one standing" is false for every pod
  in the batch and each brought its own. `Jukebox.FinalizeInit` sweeps up any
  extras a save already has.
- The click is read in `MapComponentOnGUI` off `UI.MouseCell()`, the way
  `UI/CoreTip.cs` reads the core's. It cannot go through selection:
  `StripInteraction` turns away every `Selector.Select` that is not a colonist,
  so the jukebox would never see a click.

## The sound is the daemon's

The mod decides and the daemon plays. Both stations are the same kind of thing to
it - the built-in track is an absolute path, the station is a URL - so there is
one mechanism rather than two taking turns.

- `Sim/Radio.cs` sends `{"t":"audio","source":...,"volume":...}` on a change only,
  and re-sends on a reconnect because the mod is the only thing that remembers
  what was playing. A `volume` with **no `source` key at all** is the slider
  moving and must not restart the stream; `source: null` is a stop. Three cases,
  one message - see `some_option` in `api.rs`.
- `slopd/src/audio.rs` opens the source on its command thread, so a station that
  will not answer is an error attached to the pick that caused it, then hands a
  feeder thread the decoding. Samples cross to the audio callback through a
  bounded ring; an empty ring answers silence, never `None`, because ending the
  source there would end the music for good.
- `Event::Audio` goes out on connect and on change, polled off the player every
  half second. `Radio.Report` acts on a failure once and hands back to the OST.
  `GET /api/audio` says the same thing to a person with `curl`, which is the way
  to tell "the mod never asked" from "the daemon could not" without the journal.
- **Never `open_default_sink`, and never ALSA's `null`.** That helper falls back
  by walking every output device and taking the first that opens, and `null` -
  "Discard all samples" - sorts first and always opens. It reports success, makes
  no sound, and shows nothing in `pavucontrol` because it never goes near the
  sound server. rodio tries to filter it on `driver != "null"`, which does not
  match on ALSA. `open_device` names what it wants instead: the PCM **id** from
  `Device::id()` (`description().name()` is a human sentence and no use for
  matching), `pipewire` then `pulse` then `default`, and a failure stays a
  failure. `cargo test lists_output_devices -- --ignored --nocapture` prints the
  table with the trap marked.
- **Never `Player::clear`.** It pauses the player - and nothing appended to a
  paused player is ever pulled, so it is silence with no sign of itself, not even
  a stream in `pavucontrol` - and it blocks until the current source ends, which a
  station does not. Switching stations is a generation bump instead: the old
  `Ring` answers `None` on the next callback and the player moves to the source
  appended behind it.
- Volume is `Prefs.VolumeMusic * Prefs.VolumeMaster`. Master is multiplied in
  here, unlike when this played in-game: out there it is not behind the game's
  `AudioListener`, so this is the whole of it.
- `MusicManagerPlay.disabled` is now held **permanently** rather than switched
  back and forth. A load or a new colony clears it, so it is re-asserted every
  frame.

`Defs/Songs.xml` still declares `SlopWorld_Bg1`, which nothing plays any more -
the ogg it points at is now opened by path. It is left in place because
`Patches/RemoveVanillaSongs.xml` is written around a `SlopWorld_` song existing;
both can go together when somebody is in there anyway.

## Why it is not in the game

Four runs of the game found four walls, in this order:

- **aac connects and then produces nothing.** Unity's audio is FMOD, which takes
  AAC from the platform's own decoder - iOS, Android, the consoles - and has none
  on desktop. `AudioType.ACC` is in the enum and the request goes out fine, which
  is what makes it look like it should work.
- **http is refused before it is sent.** Unity 2022 will not send a cleartext
  `UnityWebRequest`. The opt-in is `PlayerSettings.insecureHttpOption`, baked into
  the player at build time, and this build's `UnityWebRequestModule` carries no
  per-request override, only the `InsecureConnectionNotAllowed` error code.
- **https is refused after it is sent.** With `streamAudio` on, the request only
  hands FMOD the url and FMOD does the fetching - and FMOD has no TLS.
- **A loopback relay got the bytes there and it still would not play.** Unity's
  ban is on the host and loopback is exempt, so a `TcpListener` in the mod piping
  the station's own http through did deliver: `240971/? (55.85%) bytes downloaded
  but size is still not known`. That last clause is the wall. Nothing begins
  playback without a `Content-Length`, and an Icecast stream has none.

The presets are the rates the host answers on: **32, 128, 192, 320**. 64 and 96
are quoted around the web and 404 here.

`cargo test -- --ignored` covers the rest: `decodes_the_station` needs the network
but no output device, which makes it the one to reach for when a station has gone
quiet, and `plays_the_station` needs speakers too.

## Which station is remembered where

In `SlopSettings.radio`, not in the save, for the reason the terminal's font is
there - see [mod-settings](mod-settings.md). The daemon keeps no memory of it at
all: it is a machine, and the mod is where the choice lives. The value is the stream's own name -
`"ost"`, or the path the preset is served at, `"mp3-192"` - so there is no second
field to keep in step with the list of presets, and a preset this build no longer
lists reads as the OST. `Radio.Read` pulls it once per session, because the
settings are not loaded when the class is first touched.

## The texture

`mod/Textures/SlopWorld/Jukebox.png`, baked from 📻 by
`python3 tools/emoji.py --emoji 📻 --name Jukebox --size 128 --color --out
mod/Textures/SlopWorld`. `--color` is new: the baker's mono mask is right for a
tinted UI icon and wrong for a thing on the ground, where a silhouette is a box
rather than a radio. See [build-commands](build-commands.md).
