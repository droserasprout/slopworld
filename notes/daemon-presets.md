# Preset ownership and resolution

`presets.rs` owns built-in/user catalogs and snapshots; `presets/edit.rs` owns
serialized mutations. Storage belongs to [configuration stores](daemon-config-stores.md).
Preset responses use an explicit `source` projection; new serialized fields do not
automatically extend the API response.

`config/resolution.rs` selects the implicit global preset, command dependencies,
and session additions, expanding dependencies first. Project mounts are separate
from preset selection. `sandbox/` validates and consumes the resolved set; cyclic
dependency graphs are rejected.

Mount policy and host-escape boundaries belong to [sandbox isolation](sandbox-isolation.md).
`slopworld-dev` owns read-only game build inputs and compiler-cache dependencies.
It uses `rust-sccache`, including the writable installed toolchains inherited
through `rust-cache`.
`slopworld-debug` extends it with writable installation/profile paths and host
capabilities; read-write game binds override the base read-only binds.
The debug preset's read-only endpoint bind exposes the daemon root credential;
read-only file access does not restrict credential use. Public configuration
instructions belong to [Sandbox presets](../docs/src/sandbox/sandbox-presets.md).

Preset `cache` paths declare shared host directories. `sandbox/preset_cache.rs`
resolves and guards them for previews; lifecycle launch prepares directories only
after constructing a valid plan, using the cache paths retained by that plan so
catalog changes cannot separate preparation from its mounts. Bind assembly includes
them read-write even when they do not yet exist. Ordinary `rw` paths remain existing-only because they can
name files and sockets. Cache contents outlive sessions.

Built-in download/compiler cache presets use `cache` for disposable shared data.
Installed toolchains, application state, files, and sockets retain their existing
mount semantics. Platform-specific Xcode paths remain existing-only so Linux
launches do not create macOS directory trees.

SSH presets grant file and socket access; they do not override OpenSSH agent
selection. `ssh` mounts client configuration and known hosts, `ssh-agent` mounts
and forwards the daemon's `SSH_AUTH_SOCK`, and `1password` mounts the 1Password
socket directory. An explicit `IdentityAgent` in the mounted configuration wins
over `SSH_AUTH_SOCK`, so the presets must expose that selected socket. User-facing
selection guidance belongs to [sandbox presets](../docs/src/sandbox/sandbox-presets.md#ssh-agents).

Preset `warning_level` declares `none`, `sensitive`, or `host_control`; `escapes`
explains the access. These are descriptive warnings, not permission gates. Legacy
nonempty explanations retain at least sensitive severity. Generated `tmux` and
`daemon_config` capabilities force host-control severity in the API projection and
client model. `Client/Catalog/PresetWarnings.cs` resolves the maximum severity and
source explanations over the supplied catalog's dependencies, including template
snapshots. Selected configurations include the implicit global preset. Locked
required rows retain their warning color; root capabilities cannot be downgraded by
editing warning metadata. Missing references keep their separate invalid-state UI.
