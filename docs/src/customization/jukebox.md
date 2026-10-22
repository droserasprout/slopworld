# Jukebox

Open the map jukebox or status-bar jukebox control. The OST is a built-in source;
**Settings > Audio > Sources** controls its visibility.
The jukebox arrives with the player in a drop pod during the opening scene,
even without any agents.
If the daemon cannot open or play the OST, RimWorld plays the bundled tracks locally.
The first launch of a new profile starts the OST with **pace**, played locally.
Local playback advances to the next track without RimWorld's random pauses,
just like daemon playback.

## Native Linux playback

<img src="../images/jukebox-menu.jpg" alt="Jukebox menu with playback controls and radio sources" align="right" width="280">

Add a radio station with **Settings > Audio > Add source**. Edit or remove sources
in the Sources table. ICY metadata supplies the track display. Volume multiplies
RimWorld's audio settings; mute stops daemon playback and downloads.

The full menu offers song recognition through `songrec`, Like, and History.
Likes are saved locally; see [Paths and files](../reference/paths.md) for stations
and liked-song locations.

## Spotify via ncspot

This feature requires a native Linux daemon, `ncspot`, and Spotify
Premium. The source is hidden when ncspot is unavailable; hide it manually through
Settings if desired.

1. Install `ncspot`.
2. Choose **Play → Spotify (ncspot)**.
3. Complete ncspot's login.
4. Choose music in its terminal.

Muting or switching to OST/radio closes `ncspot`. Unmuting starts it again. Use its
terminal to resume or choose music. **Stop on exit** also applies. Playback and
Spotify library controls remain in `ncspot`. "Like" button writes only SlopWorld's local likes.

## Sidecar limitations

Sidecar mode uses RimWorld to play the bundled OST.

Internet radio, song recognition, and Spotify playback are not available.
