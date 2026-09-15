# Jukebox boundaries

`Sim/Jukebox/Radio.cs` bridges game audio settings and the daemon; `audio/` owns decoding and
playback. Station catalogs are user-owned TOML, not shipped content. Stable keys cross the
wire; stream URLs remain daemon-side. See the book for catalog configuration.

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
