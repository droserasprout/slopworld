# Selectable audio output

Status: proposed

## Problem

OST currently plays on the client, while internet radio plays on the daemon host.
slopweb users need a simple choice of where either source plays, including when
several browser clients connect to the same daemon.

New client playback and output selection are scoped to slopweb. Native SlopWorld
keeps its existing bundled OST and daemon radio/Spotify behavior; adding a native
C# radio decoder or native output selector is out of scope. Shared application
code may need changes to support the browser path without regressing native use.

The normal deployment is one person using several devices. Shared playback uses
simple latest-command-wins behavior, without controller ownership or conflict
resolution. The slopweb demo is an ordinary VM instance shared with friends, where
independent local playback is useful. Do not hardcode demo behavior or introduce
a demo-specific shared-playback restriction.

This plan lives in the repository root at the user's request.

## Agreed behavior

Offer two output choices in slopweb, remembered independently by each client:

| Output | slopweb | Multiple clients |
| --- | --- | --- |
| This device — browser (default) | Browser audio plays hosted OST or relayed radio. | Each client controls its own source, volume, and playback independently. |
| Server — hostname (shared) | Controls the daemon player. | One shared player; the latest command wins. |

Place these choices in a nested `Output` submenu in the existing Jukebox dropdown,
with the selected output marked. Identify the browser client by its browser name,
and the server by its daemon-reported hostname.
Use a generic device/browser label if identity is unavailable; do not mistake the
web page's hosting hostname for the browser device's hostname. Names are display
labels, not persisted output identities.

Selecting an output alone does not start playback. Switching outputs stops the
client's local player, but does not stop shared server playback. An explicit play
command starts or replaces playback at the selected destination; explicit stop
affects that destination. Display the shared player's actual state when selected.

The daemon owns the resumable shared selection. Pausing retains the selected OST
track, station and stream, or Spotify source in the daemon snapshot. Play from any
client uses the daemon's selection at the time it accepts the command. Shared
transport commands preserve the current shared volume.

Connecting, reconnecting, receiving snapshots, and selecting server output are
read-only: they must not replay saved selections or push a client's saved volume
to the shared player. Keep local source and volume preferences separate from shared
state. Returning to local output restores the selection and volume but leaves
playback stopped. Startup restoration must not start shared playback. Browser
playback permission applies to local OST startup.

Retain an explicit stop-on-exit preference for server output. It applies only when
that output is selected at orderly client shutdown; document that it stops the
shared player even if another device last changed it. Closing a local listener,
losing a connection, or having a saved local mute preference never stops the server.

Spotify remains server-only through the existing daemon-managed ncspot player.
Explicitly choosing Spotify switches and persists output to server, stops local
playback, and starts/opens the daemon's Spotify player. This source action is the
exception to output selection alone not starting playback. Validate ncspot and
host-audio availability before switching; if unavailable, explain why and leave
the current output unchanged. Restoring a saved selection or receiving a snapshot
must not trigger this switch. Local playback does not relay Spotify audio.
Switching away from server output leaves Spotify playing there.

No remote client selection, controller ownership, playback transfer, or synchronized
listening. Local playback must never send a global daemon stop merely to start OST.

## Open question

Today a fresh tab waits for the daemon to report idle before starting local OST.
Should a fresh tab on `This device` keep that startup rule or start local OST
independently of daemon state? This decision concerns fresh-tab startup only.

## Implementation plan

1. **Separate local playback from shared daemon control.** Extend the jukebox
   application model with a persisted output preference. Route source and transport
   actions to the selected player. Keep local state independent of daemon audio
   snapshots. Retain stop acknowledgement only where a real shared-player source
   transition needs it. Ensure disconnect and shutdown of local listeners cannot
   stop the shared player. Implement the shared-output stop-on-exit rule above.
   Replace reconnect-driven shared selection/volume writes with explicit commands
   and snapshot observation. Extend the shared audio snapshot to carry the
   resumable selection while paused. Resolve Play and navigation against that
   selection in the daemon, and allow transport requests to omit volume.

2. **Use compressed passthrough and browser decoding.** Relay encoded
   audio unchanged, with ICY metadata extracted separately. No server transcoding
   or raw PCM transport. Use browser media-element decoding; handle unsupported
   streams as ordinary playback errors. Relay support is not limited by the
   daemon's decoder features because the relay does not decode audio. Prefer a
   compatible alternative stream from the same station; otherwise report an
   unsupported format without changing output. Test relay byte preservation,
   cancellation, and bounded buffering with fixtures and simulated live streams;
   use game-free browser-adapter contracts for lifecycle behavior. Actual browser
   playback verification needs a separate user request.

3. **Add a radio relay to slopd.** Resolve station and stream IDs through the existing
   catalog and serve audio over an authenticated streaming HTTP endpoint. Relay
   requests must not open the daemon audio device or alter shared playback. Cancel
   upstream work when the listener leaves. Start with independent upstream requests
   per listener; sharing connections is optional later work. Preserve track metadata
   through a client-consumable mechanism without changing shared-player metadata.
   Scope metadata and delayed work to the listener's playback generation. Separate
   stream fetching/metadata extraction from audio-device ownership so the relay
   works in sidecars. Expose a same-origin media route on the slopweb host. The
   host authenticates its request to slopd with its existing daemon credential
   and streams the audio response with its media content type. The browser sets
   `audio.src` directly to the host route. Daemon credentials stay on the host.

4. **Support browser radio playback.** Extend browser audio to consume the relay
   with bounded buffering, cancellation,
   volume, pause, and error reporting using the chosen decoding contract. Pause
   disconnects live radio and releases buffers; play reconnects at the live edge.
   Handle the initial user gesture required for browser playback.

5. **Support daemon OST playback.** Package authored OST assets with the daemon and
   expose playback by validated track ID, not arbitrary file paths. Extend the
   daemon player and shared state with track identity and OST navigation. Keep asset
   identity/order consistent with native SongDefs and browser assets through one
   canonical manifest. Use the same daemon output routing as radio.

6. **Expose the nested output selector.** Add the named `This device` and `Server`
   choices to slopweb's Jukebox dropdown's `Output` submenu, using the
   identity rules above. Advertise radio relay capability separately from host audio
   output. A sidecar can relay radio without speakers; hide or disable unavailable
   server playback with an explanation. Retain existing volume/device routing
   settings for server radio/OST; ncspot retains its existing host routing.
   Make explicit Spotify selection switch to server output after capability
   validation. Apply the same behavior on personal machines and the friends' VM;
   no demo-specific branch or control restriction.

7. **Verify and document.** Add focused game-free tests for routing, independent
   clients, relay cancellation, shared-player latest-command behavior, stale async
   completion, output switching, and shutdown. Run applicable just recipes. Update
   jukebox ownership and user guidance once behavior is implemented. Do not launch
   the game or browser without a separate user request.

## Completion requirements

- slopweb can play OST and browser-supported radio streams on this device.
- Native SlopWorld retains its existing playback behavior without a new local
  radio decoder or output selector.
- A daemon with audio output and OST assets can play either source through its
  shared player, controllable from slopweb.
- Two local listeners can choose different sources without interrupting each other
  or the server player.
- Shared controls show current shared state and apply commands in daemon-accepted
  order; stale asynchronous work cannot restore an older selection.
- After one browser pauses a shared OST track, a newly connected browser sees the
  paused track and can play it at the current shared volume.
- Connecting, reconnecting, and selecting server output leave shared source and
  volume unchanged. Local state survives output switches without automatic play.
- Output preferences persist per client; unavailable outputs produce clear errors
  rather than silently moving sound elsewhere.
- Closing or pausing a local listener releases its stream resources without stopping
  another listener or shared server playback.
- Browser radio through a sidecar works without requiring a daemon audio device.
- A game-free test reads audio through the same-origin host route and checks its
  media content type. Closing that response cancels the station request even
  while the station is silent.
- Unsupported codecs produce actionable errors or use a compatible stream from
  the same station; they never silently switch playback destinations.
- Radio uses compressed passthrough without server transcoding or PCM transport.
- slopweb's Jukebox dropdown contains a nested output selector with browser name
  and server hostname labels.
- Explicit Spotify selection switches to server output and starts/opens ncspot;
  unavailable Spotify leaves the current output unchanged.
- The friends' VM uses the same capabilities and playback behavior as other hosts.
- Server stop-on-exit follows the explicit preference only while server output is
  selected; disconnects and local mute state cannot trigger a shared stop.
- Relevant checks pass and focused documentation describes the implemented behavior.
