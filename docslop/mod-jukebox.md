# The jukebox

A radio set rides down in the pod with the first clanker. LMB on it opens four
rows: **Play** (carrying what is on), **Mute**, **Stop on exit**, **Settings**.
Play opens the OST and the stations - "RadioParadise Main", "WeFunk Radio",
"Classic Vinyl HD" - and
each station's row opens a third menu of the quality presets it serves; whatever
is playing is marked. `Sim/Jukebox.cs` is the box, `Sim/Radio.cs` is the sound and
the list of stations.

A `FloatMenuOption` holds no children, so each nested list is a second `FloatMenu`
opened from the parent row's action. That is safe because `FloatMenuOption.Chosen`
calls `PreOptionChosen` - which closes the parent - before it invokes the action.

- **Mute** is the only off switch this box has, so it is a **stop** rather than a
  volume of zero: `Radio.Source` answers null, which the daemon reads as silence.
  Nothing downloads for nobody, and a station comes back where it *is* rather than
  where it was left, which is what a radio does anyway. Picking a station clears
  it - a row that does nothing because of a tick two rows down is a row nobody can
  explain - and `Radio.Report` says nothing while it is on, "not playing" being
  the answer that was asked for.
- **Stop on exit** (on by default) is a `Root.Shutdown` prefix,
  `Patch_RadioOnShutdown`, sending `source: null` while the socket is still up -
  `MiniWebSocket.SendText` writes on the calling thread, so the bytes are in the
  kernel before the process goes. `QuitInterceptor` routes the window's close
  button through `Root.Shutdown` too, so that is the whole of an orderly quit. A
  **killed** game is not covered and cannot be: nothing of ours runs. `GET
  /api/audio` is how to see that, and turning the box back on is how to fix it.
  `Radio.Quit` latches `_quit` and `Radio.Update` returns on it: `Root.Shutdown`
  does *not* end the process where it is called - `Application.Quit` lets the
  frame finish and the save is written over the frames after it - so Update runs
  on, and it used to put the station straight back on the next one. The music
  stopped for a second and came back.
- **Settings** opens vanilla's Audio category - `SlopOptions.OpenAudioTab` - since
  that is where the sliders `Radio.Volume` multiplies actually live. It opens the
  options dialog when none is up and swaps the tab when one is.
- **Hovering the box** names the song, `♪ Artist - Song`, in vanilla's own
  bubble: the thing is not selectable and has no inspect pane to put it in.
  `Jukebox.Tip` off `TooltipHandler.TipRegion`, keyed on the cell so the bubble
  does not restart its fade every time the station moves on. Nothing is drawn when
  there is nothing to say - muted, or a station a second into a pick. The OST's
  name is hardcoded (`Radio.OstTitles`, "Terry Fail - slopbg01" / "Terry Fail - slopbg02"), there being nobody
  else to say it. **Hovering the status bar's icon** says the same thing behind
  the name of what it opens - `Jukebox.IconTip`, "Jukebox" and then the song on a
  second line, the name alone when nothing is playing - the icon being a door onto
  this box and so owing the same answer. `TopBar` keys that one on an id of its
  own for the reason the cell is used here.
- The two ticked rows are `SlopWidgets.MenuToggle`, which hangs a tick or a cross
  off `FloatMenuOption.extraPartOnGUI` with `extraPartRightJustified`. `Disabled`
  would have been the nearest vanilla thing and it reads as broken rather than as
  off. The marks are `Icons.Check` and `Icons.Cross` like the rest of them.

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
- **`Jukebox.OpenMenu` is the menu, and the box is one of two callers.** The other
  is the status bar's icon ([mod-ui-chrome](mod-ui-chrome.md)): nothing in those
  four rows is about where the press came from, so the whole of it is static and
  the two callers differ only in what they had to do to be heard. The nested
  lists go out through `TerminalWindow.OpenOverPane` for the same reason - the bar
  is drawn over a terminal as well as over the map, and a menu opened from it
  belongs above both. `CoreTip.OpenMenu` is the same move for the persona core,
  where the one thing that *was* about the click - the cell the hint bubble pins
  to - is looked up off the lister instead.

## The sound is the daemon's

The mod decides and the daemon plays. Everything on the list is the same kind of
thing to it - the built-in track is an absolute path, a station is a URL - so there
is one mechanism rather than one per source taking turns. The daemon knows nothing
about which stations exist; adding one is a line in `Radio.Stations`.

- `Sim/Radio.cs` sends `{"t":"audio","source":...,"volume":...}` on a change only,
  and re-sends on a reconnect because the mod is the only thing that remembers
  what was playing. A `volume` with **no `source` key at all** is the slider
  moving and must not restart the stream; `source: null` is a stop. Three cases,
  one message - see `some_option` in `api.rs`.
- `_told` is not redundant beside `_sent`. Silence is a thing to *send* - muted,
  the source is null - so a null `_sent` cannot also stand for "not told yet", and
  it did until Mute existed: a mute right after a `Push` sent nothing at all.
- `slopd/src/audio.rs` opens the source on its command thread, so a station that
  will not answer is an error attached to the pick that caused it, then hands a
  feeder thread the decoding. Samples cross to the audio callback through a
  bounded ring; an empty ring answers silence, never `None`, because ending the
  source there would end the music for good.
- **The title is spliced into the audio**, which is why `Icy` exists. `Icy-MetaData:
  1` on the request gets an `icy-metaint` back, and from then on the body is that
  many bytes of audio, one length byte, that many *sixteens* of
  `StreamTitle='...';StreamUrl='...';` padded with NULs, and around again. `Icy`
  wraps the **raw** body - before any buffering, or the byte count is out of step -
  hands on the audio and keeps the text. Getting it wrong is not a missing title,
  it is a click every few seconds. A zero length byte, which is nearly all of them,
  means nothing changed; the closing quote is found as `';` because an apostrophe
  in a song title is ordinary. It lands in `AudioState.title` through a `TitleSink`
  that checks the generation, so a station being switched away from cannot name the
  one that replaced it. RP and WeFunk both serve `icy-metaint: 16000` today; a host
  that stops is a jukebox with no title and nothing else wrong, and
  `cargo test names_what_the_station_is_playing -- --ignored --nocapture` is how to
  tell. Run it **alone**: the three ignored tests share `GENERATION`, and
  `plays_the_station` bumping it beside this one makes its `TitleSink` stale, which
  reads as a station that named nothing.
- `Event::Audio` goes out on connect and on change, polled off the player every
  half second. A title changing is a change, so the mod hears each new song on the
  same beat as everything else. `Radio.Report` acts on a failure once and hands back to the OST.
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
- **A ring must declare a finite span, and never `None`.** The mixer wraps whatever it
  is given in rodio's `UniformSourceIterator`, which builds the channel and rate
  converters from the source's own `channels()` and `sample_rate()` - and rebuilds
  them **only at a span boundary**. `None` means "one span, for ever", so the
  converters built for the first stream of the session stayed bolted on to every
  stream appended after it: the device is opened once and outlives every switch.
  That is a station playing at the wrong speed. Mono read as stereo is **2x** -
  WeFunk's 64k - and RP's 32k, which is 22050 **mono**, read as 44100 stereo is
  **4x**. Stereo 44100 was every stream on the list until WeFunk arrived, which is
  why nothing had shown it; RP's 32k had been wrong the whole time and nobody had
  picked it. `Ring::current_span_len` answers `SPAN`, frame-aligned - unaligned and
  the channel converter loses which sample belongs to which side.
  `a_station_of_any_shape_plays_at_its_own_speed` is the regression test and needs
  neither network nor device: it appends four shapes to one player and reads the
  speed off a ramp, measuring the **distance between two points on it** so that the
  mixer's lead-in silence and the converters' tail flush cannot be mistaken for it.
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

## The stations

`Radio.Stations` is the list, in menu order. Each carries its name, the rates it
answers on, a host and a path format, and the rate it was last left on - **per
station**, so a switch away and back comes up where it was: the lists do not
overlap, and RP's 192 is not a rate WeFunk has ever served. mp3 throughout rather
than the aac some of them lead with, aac being what the game could not decode.

- **RadioParadise Main**, `stream.radioparadise.com/mp3-{rate}`, at **32, 128, 192,
  320**. 64 and 96 are quoted around the web and 404 there. 128 and up are stereo
  44100; **32 is mono at 22050**, which is not a detail - see the span note above.
- **Classic Vinyl HD**, `icecast.walmradio.com:8443/classic`, at **320** and nothing
  else - no other name on that host answers. Stereo, but at **48000**, the only thing
  on the list that is not 44100 and so the only one that resamples *down*. Its path
  carries no `{rate}` at all, a station with one stream having no rate to put
  anywhere; a format string with nothing in it formats to itself.
- **WeFunk Radio**, `s-00.wefunkradio.com:8443/wefunk{rate}.mp3`, at **64** and
  nothing else - every other rate 404s and the `.m3u` names the same one stream.
  **Mono**, 44100, which is why `start` takes the channel count off the source
  rather than assuming the two RP has. Its `radio.pls` lists four mirrors - s-00,
  s-09, s-14, s-17 - shuffled per request, all serving that stream. One is named in
  the table rather than the playlist: the daemon opens a URL and decodes what comes
  back, and teaching it to unpick a playlist first would be a second fetch and a
  second thing to go wrong. A mirror that is down is the same failure as a station
  that is down, and `Radio.Report` already hands that back to the OST.

A station serving one quality still gets a submenu of one rather than a special
case. A row that plays on one station and opens a menu on the next is a row nobody
can predict, and what it answers on is worth saying either way.

`cargo test -- --ignored` covers the rest: `decodes_the_station` needs the network
but no output device, which makes it the one to reach for when a station has gone
quiet, and `plays_the_station` needs speakers too. The first two walk `STATIONS` in
`audio.rs` - one preset per station, with the shape each decodes to - kept in step
with `Radio.Stations` by hand, being a diagnostic pointed at the open web rather
than a second copy of the menu.

## Which station is remembered where

In `SlopSettings.radio` - with `radioMute` and `radioStopOnExit` beside it - not in
the save, for the reason the terminal's font is there - see
[mod-settings](mod-settings.md). The daemon keeps no memory of it at
all: it is a machine, and the mod is where the choice lives. The value is the
stream's own name - `"ost"`, or the path that preset is served at, `"mp3-192"`,
`"wefunk64.mp3"` - so it is the station's key and the preset's at once, and there is
no second field to keep in step with either list. `Radio.Read` scans every station's
rates for a match; anything else, a station or a preset this build no longer lists
included, reads as the OST. That is also why saves made before WeFunk existed come
back on the right RP preset - the key never changed. `Radio.Read` pulls it once per
session, because the settings are not loaded when the class is first touched.

## The texture

`mod/Textures/SlopWorld/Jukebox.png`, baked from 📻 by
`python3 tools/emoji.py --emoji 📻 --name Jukebox --size 128 --color --out
mod/Textures/SlopWorld`. `--color` is new: the baker's mono mask is right for a
tinted UI icon and wrong for a thing on the ground, where a silhouette is a box
rather than a radio. See [build-commands](build-commands.md).
