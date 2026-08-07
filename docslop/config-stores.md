# The two config stores, and the seams between them

Where each half keeps its knobs, and the three places they rub. See
[paths](paths.md) for the bare list of locations.

| | Daemon | Mod |
| --- | --- | --- |
| File | `~/.config/slopworld/config.toml` | `<profile>/Config/Mod_SlopWorld_SlopWorldMod.xml` |
| Path from | `dirs::config_dir()`, `SLOPD_CONFIG` overrides | the profile, named for the mod folder and the `Mod` subclass |
| Format | TOML, one `toml::from_str` | RimWorld `Scribe_Values` |
| Scope | this **machine** | this **install** |
| Written by | `Config::save`, and the HTTP routes | `ModSettings.Write` in `PostClose` |
| Sidecar | `presets/*.toml` (`SLOPD_PRESETS`) | - |

The mod never opens the TOML: it asks over HTTP (`GET /api/config` ->
`SlopConfig.FromJson`, `PUT /api/config/values`), plus the raw-text door and the
per-list routes. See [wire-protocol](wire-protocol.md), [mod-client](mod-client.md),
[mod-settings](mod-settings.md).

## Where it rubs

1. **The address is stated twice.** `daemon.bind` and `daemon.token` in the TOML;
   `host`, `port`, `token` in mod settings. Nothing keeps them in step, and drift
   reads as "the mod will not connect". `SlopConfig` carries `Bind`/`Token` it
   never draws only so a save does not clobber them, and
   `ConfigPage.DoConnectionNote` has to explain the split to the player.
2. **`SlopConfig` is a schema mirror kept by hand in a second language.** Writes
   are section-granular, so a field it leaves out is reset: silently for the ones
   with a serde default (`game_cmd`, `usage`, `history_limit`,
   `claude_credentials`, `openrouter*`), as a 422 for the ones without (`bind`,
   `tmux_socket`, `poll_ms`). Three sets of defaults - `fn default_*`, the
   `Scribe_Values` literals, the `AsString(...)` fallbacks - agree by discipline
   and nothing else.
3. Two formats and two lifetimes, which is **not** a fault: the TOML outlives a
   profile rebuild, and the pane's half stays editable with the socket down.

`GET /api/config` never carries the token as written: a set one reads as
`TOKEN_REDACTED` (`<redacted>`) in both the raw `text` and the parsed `values`
(`Config::redacted`, `redact_token_text`), an empty one stays empty so "no auth"
still reads straight. The write is the mirror: a token that comes back as the
sentinel is restored to the stored one (`replace_config`, `update_sections`), so a
save from a client that only ever saw the sentinel - the raw editor, the GUI - cannot
blank auth it never held. A real value, or an empty string to turn auth off, is any
value that is not the sentinel and stands. Which is why `SlopConfig` carrying `Token`
(seam 1) is now belt-and-braces: the daemon would restore it regardless.

## Unbuilt

Not done, argued once so it need not be argued again.

- **One source for the endpoint.** slopd writes `~/.config/slopworld/endpoint.json`
  on startup (`{"url", "token"}`); the mod already has `Json` and still needs no
  TOML parser. Mod settings keep host/port/token as an *override*, blank meaning
  "read the file", and `bind` stops being round-tripped.
- **Merge, not replace, on `/api/config/values`.** Take a `toml::Value`, deep-merge
  onto the loaded config, re-parse to validate. Kills seam 2 outright and lets
  `SlopConfig` shrink to what it draws. Best line-for-line of anything here.
- **A `[ui]` section, stored opaquely, is the wrong direction for now.** It would
  make the pane's look survive a profile wipe and serve a second client, at the
  price of a round-trip per theme, fold and sidebar drag - and the sidebar writes
  on every drag. Worth it when a non-RimWorld client is real, not before.
- **Codegen `SlopConfig` from the Rust structs** is the thorough answer to seam 2.
  The merge route gets most of it for none of the machinery.
