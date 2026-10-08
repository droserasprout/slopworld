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
The debug preset's read-only endpoint bind exposes the daemon root credential;
read-only file access does not restrict credential use. Public configuration
instructions belong to [Sandbox presets](../docs/src/sandbox/sandbox-presets.md).
