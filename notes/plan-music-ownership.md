# Music ownership consolidation

Status: implemented

Playback policy was split between socket commands, HTTP handlers, startup, tmux
metadata and the ncspot player. Keep source changes behind the music owner and
ncspot mechanics private to it. Keep Spotify client capability fallback and terminal
opening together in the existing Radio partial.

Preserve protocol fields, routes, saved selections, marker-based adoption, volume
recovery, bounded IPC and ordered launch/stop behavior. Source changes and player
polling retain distinct operation guards; no transport caller manages those guards.

Human review is pending. Review music selection and locking first, then player
recovery and the client capability/late-reply tests.
