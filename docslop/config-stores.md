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
| Sidecar | `presets/*.toml` (`SLOPD_PRESETS`), `endpoint.json` (`SLOPD_ENDPOINT`) | - |

The mod never opens the TOML: it asks over HTTP (`GET /api/config` ->
`SlopConfig.FromJson`, `PUT /api/config/patch`), plus the raw-text door and the
per-list routes. See [wire-protocol](wire-protocol.md), [mod-client](mod-client.md),
[mod-settings](mod-settings.md).

## Where it rubs

1. **The daemon owns the live endpoint.** It writes `endpoint.json` atomically
   with `url` and `token`, mode `0600`; the mod reads that descriptor rather than
   maintaining a second connection configuration.
2. **Config writes are patches.** The settings pages send only the fields in their
   read model. The daemon deep-merges the JSON object, validates the resulting TOML,
   and atomically replaces the file. Fields and sections unknown to the mod survive.
3. **`SlopConfig` is a read model, not a whole-schema mirror.** It contains only the
   fields drawn by the config and usage pages; it does not carry endpoint, project,
   session, state-rule, or sandbox-preset data.
4. Two formats and two lifetimes, which is **not** a fault: the TOML outlives a
   profile rebuild, and the pane's half stays editable with the socket down.

`GET /api/config` never carries the token as written: a set one reads as
`TOKEN_REDACTED` (`<redacted>`) in both the raw `text` and the parsed `values`
(`Config::redacted`, `redact_token_text`), an empty one stays empty so "no auth"
still reads straight. The write is the mirror: a token that comes back as the
sentinel is restored to the stored one (`replace_config`, `patch_config`), so a
save from a client that only ever saw the sentinel - the raw editor, the GUI - cannot
blank auth it never held. A real value, or an empty string to turn auth off, is any
value that is not the sentinel and stands. The endpoint descriptor is the mod's
normal path for this secret; the settings pages no longer round-trip the token.

## Remaining work

Not done, argued once so it need not be argued again.

- **A `[ui]` section, stored opaquely, is the wrong direction for now.** It would
  make the pane's look survive a profile wipe and serve a second client, at the
  price of a round-trip per theme, fold and sidebar drag - and the sidebar writes
  on every drag. Worth it when a non-RimWorld client is real, not before.
- **Codegen `SlopConfig` from the Rust structs** remains the thorough answer if a
  second client needs the complete schema. The patch route avoids requiring it for
  the current UI.
