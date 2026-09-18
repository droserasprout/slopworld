# Presets and launch resolution

Shipped presets are compiled from `slopd/presets/*.toml`; user files in `sandbox_presets/` and
`app_presets/` replace entries by name. The API and UI use those exact kind names.
`presets.rs` owns loading and validation, `Config::sandbox_of` owns dependency expansion,
and `sandbox/` lowers the result into launch arguments. See [isolation](sandbox-isolation.md)
for security boundaries; the preset files are the capability inventory.

`global` is implicit, before command/project/session additions. It is not a project checkbox.
Dependencies are cycle-safe; deleting an override reveals its builtin. Unknown fields reject
a preset file so obsolete definitions cannot silently become partial current definitions.

A command preset supplies sandbox dependencies; a literal command without a preset does not.
Choosing a shell does not share its host dotfiles: the separate userdata presets are opt-in.
Required capabilities must remain visible but unselectable in the editor.

Mount order matters: skeleton first, ordinary binds before private overlays, resolver last.
Unset variables invalidate the whole path rather than leaving an empty component that could
expand to `/`. Device binds and socket-directory binds are not interchangeable with files.
Environment starts empty; configuration-root overrides such as `CODEX_HOME` must not redirect
a tool away from its private default state.

`slopworld-debug` is an intentional host escape, including host processes, tmux, Docker,
desktop services and writable development/install paths. Its `daemon_config` capability is
a narrow read-only exception for config and endpoint files, not permission for ordinary
preset bind lists to reach protected state.
