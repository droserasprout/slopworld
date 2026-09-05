# Configuration stores

Where each half keeps its knobs, and the three places they rub. See
[paths](paths.md) for the bare list of locations.

| | Daemon | Mod |
| --- | --- | --- |
| File | `~/.config/slopworld/config.toml` | `<profile>/Config/SlopWorld.toml` |
| Path from | `dirs::config_dir()`, `SLOPD_CONFIG` overrides | `GenFilePaths.SaveDataFolderPath` |
| Format | TOML, one `toml::from_str` | TOML, flat scalar settings |
| Scope | this **machine** | this **install** |
| Written by | `Config::save`, and the HTTP routes | `ModSettings.Write` |
| Sidecar | `presets/*.toml` (`SLOPD_PRESETS`), `$XDG_DATA_HOME/slopworld/jukebox/*.toml` (`SLOPD_JUKEBOX`), `endpoint.toml` (`SLOPD_ENDPOINT`), `tasks.toml`, `prompt-summaries.toml`, `session-activity.toml` | none; the mod mirrors the daemon catalog |

The mod does not open daemon TOML. It uses HTTP (`GET /api/config`,
`PUT /api/config/patch`), the raw-text route, and per-list routes. `slopd` reads
`$XDG_DATA_HOME/slopworld/jukebox/*.toml`; `Radio` receives the catalog over the
authenticated WebSocket and sends only station/stream keys. See [wire-protocol](wire-protocol.md),
[mod-client](mod-client.md), and [mod-settings](mod-settings.md).

## Where it rubs

1. **Endpoint:** the daemon atomically writes `endpoint.toml` (`0600`) with `url`
   and `token`; the mod reads it instead of storing another connection config.
2. **Patches:** settings pages send their read-model fields; the daemon deep-merges,
   validates TOML, and replaces the file atomically, preserving unknown fields.
3. **Read model:** `DaemonConfig` contains fields used by config, Commands, and usage
   pages, not endpoint, project, session, state-rule, or sandbox-preset data.
4. **Lifetime:** daemon TOML survives profile rebuilds; profile TOML settings remain editable
   while the socket is down.

Network policy is daemon-owned: `project.network` is the default and
`session.network` may override it in either direction. The session list exposes
effective `network` and raw `network_override`; saving an agent sends only the optional override. DNS is
separate: session `dns` overrides project `dns`, omission follows up to two IPv4
nameservers in the daemon's current `/etc/resolv.conf`, and an explicit value selects
up to two servers for pasta. Network and DNS changes affect the next agent start, not
running processes.

`GET /api/config` redacts a set token as `TOKEN_REDACTED` in raw and parsed forms;
empty remains empty. Writes restore the stored token when the sentinel is sent,
while any other value, including empty, replaces it. The endpoint descriptor is
the mod's normal token path; settings pages do not round-trip the secret.
