# More radio

The next stations should widen the jukebox's geography and texture without
turning it into a generic genre directory. Good candidates are:

- [Cashmere Radio](https://cashmereradio.com/about/) - Berlin community radio:
  ambient, jazz, experimental electronics, sound art and strange spoken-word
  pieces.
- [LYL Radio](https://radio.lyl.live/) - Lyon/Paris leftfield radio with global
  selectors, unusual electronics and long-form experimental mixes.
- [Noods Radio](https://www.noodsradio.com/info) - Bristol selectors moving
  between dub, ambient, jazz, folk, soul, jungle and oddball electronics.
- [Radio 80000](https://www.radio80k.de/) - non-commercial Munich community
  radio with an art-space, open-format feel.
- [Mutant Radio](https://www.mutantradio.net/about) - Tbilisi-rooted
  independent radio for electronic music, global sounds, performances and
  cultural programming.
- [Oroko Radio](https://oroko.live/radio) - Accra-based independent radio
  spanning African, Caribbean, Latin, soul, jazz, ambient and club music.
- [boxout.fm](https://boxout.fm/) - New Delhi underground programming covering
  South Asian hip-hop, bass, electronica, indie and experimental music.

SomaFM is deliberately not a SlopWorld source. Its [direct-stream notice](https://somafm.com/deepspaceone/directstreamlinks.html)
and [Terms of Service](https://somafm.com/contact/tos.html) restrict personal
listening and prohibit new third-party applications, including games, without
explicit permission. Remove bundled SomaFM definitions unless the station gives
written approval.

Personal exception: the two former builtins may be kept as untracked local files
under `~/.config/slopworld/jukebox/` for the user's own listening. Do not commit,
ship or copy those definitions into another distributed install.

## Embedding-policy audit

This is a permission triage, not a license. `Ask first` means do not ship the
stream until the station grants written permission for a RimWorld mod/game.
“No public policy found” does not mean embedding is allowed. Recheck each page
before adding a source; policies and streams change.

| Station | Policy or official contact page | Reading for SlopWorld |
| --- | --- | --- |
| Radio Paradise | [FAQ](https://apps.radioparadise.com/rp3.php?name=Help) | The FAQ discusses listening in a shop, not game embedding. Ask first. |
| WEFUNK | [About / contact](https://www.wefunkradio.com/about) | No explicit embedding policy found. Ask first. |
| WALM / Classic Vinyl HD | [Official site](https://walmradio.com/walm/) | No explicit embedding policy found. Ask first. |
| Kiosk Radio | [About / contact](https://www.kioskradio.com/about) | No explicit embedding policy found. Ask first. |
| WFMU | [Stream information](https://freeform.wfmu.org/ssaudionet.shtml) | Direct links and listener apps are documented, but no game permission is granted. Ask first. |
| dublab | [Contact / station page](https://www.dublab.com/contact) | No explicit embedding policy found. Ask first. |
| NTS Radio | [Terms and conditions](https://www.nts.live/terms-and-conditions) | No third-party game embedding permission found. Ask first. |
| KEXP | [Terms of Use](https://www.kexp.org/terms-and-conditions/) | Personal, noncommercial site use; copying/distribution is restricted. Do not add without written permission. |
| Cashmere Radio | [About / contact](https://cashmereradio.com/about/) | No explicit embedding policy found. Ask first. |
| LYL Radio | [Station](https://radio.lyl.live/) | No explicit embedding policy found. Ask first. |
| Noods Radio | [Information / contact](https://noodsradio.com/info) | Independent and listener-supported, but no embedding grant found. Ask first. |
| Radio 80000 | [Privacy policy and app notes](https://www.radio80k.de/privacy-policy/) | Describes its own app using the stream; it does not authorize third-party apps. Ask first. |
| Mutant Radio | [About / contact](https://www.mutantradio.net/about) | No explicit embedding policy found. Ask first. |
| Oroko Radio | [Radio](https://oroko.live/radio) | No explicit embedding policy found. Ask first. |
| boxout.fm | [Terms of Use](https://boxout.fm/terms) | Personal/noncommercial use; redistribution or exploitation requires written consent. Do not add without permission. |

The first outreach targets should be Cashmere, Noods and Oroko. Request a
written game-use grant covering the live stream, station name/logo, now-playing
metadata and direct HTTPS playback from the daemon.
