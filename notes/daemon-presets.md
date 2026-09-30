# Presets and launch resolution

The build compiles supplied presets from `slopd/presets/*.toml`.
User files in `sandbox_presets/` and `app_presets/` replace entries by name. The API and UI use those exact kind names.
`presets.rs` owns loading and validation, `Config::sandbox_of` owns dependency expansion,
and `sandbox/` lowers the result into launch arguments. See [isolation](sandbox-isolation.md)
for security boundaries. The preset files list the capabilities.

`global` is implicit, before command/project/session additions. It is not a project checkbox.
Dependency resolution handles cycles safely.
Deleting an override restores the supplied preset.
The daemon rejects preset files with unknown fields.
This prevents obsolete definitions from silently becoming incomplete current definitions.

A command preset supplies sandbox dependencies.
A literal command without a preset does not.
Choosing a shell does not share its host dotfiles: the separate userdata presets are opt-in.
Required capabilities must remain visible but unselectable in the editor.

The built-in Codex command keeps `tui.animations` enabled while disabling its individual effects.
Codex CLI currently couples the Working timer's periodic redraw to that master switch, so
turning animations off freezes the timer. Keep the command override until Codex separates
timer refreshes from decorative effects.

Apply mounts in this order:

1. The basic mounts.
2. Ordinary binds.
3. Private overlays.
4. The resolver.
Unset variables invalidate the whole path rather than leaving an empty component that could
expand to `/`. Device binds and socket-directory binds are not interchangeable with files.
The environment is empty at launch.
Configuration-root overrides such as `CODEX_HOME` must not redirect a tool away from its private default state.

`slopworld-debug` is an intentional host escape, including tmux, Docker,
desktop services and writable development/install paths. Its `daemon_config` capability is
a narrow read-only exception for config and endpoint files, not permission for ordinary
preset bind lists to reach protected state.
