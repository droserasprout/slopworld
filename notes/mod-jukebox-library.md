# Jukebox local audio and history

The station catalog and daemon playback model are in [mod-jukebox](mod-jukebox.md).

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
