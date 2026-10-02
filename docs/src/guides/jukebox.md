# Jukebox

Open the map jukebox or status-bar jukebox control. The OST is a built-in source;
**Settings > Audio > Sources** controls its visibility.

## Native Linux playback

Add a radio station with **Settings > Audio > Add source**. Edit or remove sources
in the Sources table. ICY metadata supplies the track display. Volume multiplies
RimWorld's audio settings; mute stops daemon playback and downloads.

The full menu offers song recognition through `songrec`, Like, and History.
Likes are saved locally; see [Paths and files](../reference/paths.md) for stations
and liked-song locations.

## Spotify proof of concept

This experimental feature requires a native Linux daemon, `ncspot`, and Spotify
Premium. The source is hidden when ncspot is unavailable; hide it manually through
Settings if desired.

1. Install `ncspot`.
2. Choose **Play → Spotify (ncspot)**.
3. Complete ncspot's login.
4. Choose music in its terminal.

**Open Spotify player** returns to that terminal. Hiding it leaves playback
running, and the jukebox displays ncspot's current track.

Muting or switching to OST/radio closes ncspot. Unmuting starts it again; use its
terminal to resume or choose music. **Stop on exit** also applies. Playback and
Spotify library controls remain in ncspot. Like writes only SlopWorld's local likes.

## Sidecar playback

Sidecar mode uses RimWorld to play the bundled OST. It offers mute and Settings,
without the source picker, recognition, Like, History, or Spotify playback.
