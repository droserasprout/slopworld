# Jukebox boundaries

`Sim/Jukebox/Radio.cs` bridges game audio settings and the daemon; `audio/` owns decoding and
playback. The source list has built-in `ost` and `spotify` entries plus user-owned TOML station
presets. Source visibility is a mod preference; preset contents and stream URLs remain daemon-
owned. Settings edits presets through root-only daemon routes rather than reading `~/.config`
from the game process. See the book for catalog configuration.

Spotify is shown only while the native daemon reports an executable `ncspot`; it is disabled in
Audio settings and omitted from the jukebox menu otherwise. A saved Spotify selection falls back
to OST when the capability snapshot says the player is unavailable.

Native and sidecar playback differ: daemon mode disables vanilla music, while sidecar mode
uses the native manager for the SlopWorld OST. Mute must stop daemon downloads, not just set
volume to zero. Shutdown delivery needs a latch because Unity may run frames after Quit.

Audio tests use fixtures or loopback only; never contact real stations or recognition services.
Source probing runs off the audio command worker. Each request gets a generation and a
cancellation flag, so stop/replacement remains responsive and stale success, failure, and
metadata cannot commit. Opening has its own header deadline; once committed, the stream is
governed by the live body idle timeout rather than the opening deadline. Metadata observed
during probing is published only after the output commits. Transport cancellation must return
a terminal I/O error; `Interrupted` tells readers to retry and spins retired feeders.

Decoder/source generations guard late playback and metadata. A stale title is still stale
state even if the corresponding audio was discarded. [Likes and recognition](mod-jukebox-library.md)
cover the other side of that identity boundary.

The ncspot proof of concept is coordinated by `manager/ncspot.rs`: it owns a host
errand terminal, a private IPC runtime directory and bounded status reads. The tmux
`@slopworld_ncspot` marker identifies the player across redeploy; names and adopted
host commands do not. Startup restores the player after session adoption, preserving its
last applied volume without sending volume commands. If no applied-volume marker exists
(for example, recovery during login), the first successful IPC poll initializes volume.
Relative volume commands need an extra one-percent step to reach zero/full scale because
ncspot truncates each percentage step to an integer. The mod launches through WebSocket
audio selection and opens the terminal named in the audio reply; shutdown's stop uses the
same ordered connection. Source transitions share a daemon mutex with the explicit HTTP
launch API. Stop the managed terminal before starting OST/radio;
muting currently stops it too. `Radio` keeps Spotify selection separate from the
null-station OST case and ignores mismatched source metadata during transitions.
See the [tour](../docs/src/tour/fun.md#spotify-proof-of-concept) for setup and limits.
