# Preset ownership and resolution

`presets.rs` owns catalogs/snapshots; `presets/edit.rs` serializes mutations.
`config/resolution.rs` expands dependencies before session additions, including the
implicit global preset. Project mounts are separate. Sandbox validation rejects cycles.
API responses explicitly project fields; schema additions do not extend them automatically.
Test preset parsing and sandbox mechanisms, not the contents of individual shipped TOML presets.

`slopworld-dev` supplies read-only game inputs and shared compiler caches. Rustup
needs writable download/staging paths as well as toolchains/update hashes; otherwise
the read-only parent blocks installation. `slopworld-debug` adds writable installation
paths and host capabilities. Its read-only endpoint mount exposes the root credential;
read-only access does not limit credential use. See [sandbox isolation](sandbox-isolation.md).

`cache` declares shared directories that may not exist yet. `sandbox/preset_cache.rs`
resolves/guards them; lifecycle creates them only after validating a launch plan,
using that plan's retained paths so catalog changes cannot change its mounts.
Ordinary `rw` paths remain existing-only because they may name files or sockets.
Cache contents outlive sessions. Keep platform-specific paths existing-only so Linux
launches do not create macOS trees.

`sandbox/paths.rs` owns `${VAR:paths}` expansion for ordinary binds. Validation
and mount selection use the same expansion and guard every list entry separately.
Other path fields retain single-path semantics. The .NET preset keeps installations
read-only and redirects CLI writes to a persistent private `/tmp` root.
`sandbox/bind/dotnet.rs` aliases only selected NuGet mounts into that reserved CLI
home after private overlays, retaining their modes. NuGet also uses `DOTNET_CLI_HOME`
for configuration, plugins and caches. Alias sources overlapping private state
reject launch; they must never reveal the host originals behind a private overlay.

SSH presets expose files/sockets but do not override OpenSSH selection. Explicit
`IdentityAgent` wins over `SSH_AUTH_SOCK`; selected sockets must be mounted as well
as forwarded. See [SSH agents](../docs/src/sandbox/sandbox-presets.md#ssh-agents).

Warning metadata describes access rather than gating permission. Explanatory text
does not change the declared severity. Generated tmux and daemon-config capabilities
force host-control severity. `PresetWarnings` resolves
maximum severity through dependencies and template snapshots, including the implicit
global preset. Locked rows retain warnings; editing metadata cannot downgrade root
capabilities. Missing references remain a separate invalid state.

Storage belongs to [configuration stores](daemon-config-stores.md); user configuration
to [Sandbox presets](../docs/src/sandbox/sandbox-presets.md).
