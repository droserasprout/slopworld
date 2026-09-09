# TOML data review plan

Move suitable embedded records into shipped or user-editable data without weakening the
existing configuration and content boundaries.

## Boundary

Keep the three existing stores distinct:

- daemon-owned machine configuration stays in `~/.config/slopworld/config.toml`;
- mod-owned per-install preferences stay in the profile's `Config/SlopWorld.toml`;
- packaged mod content and visual catalogs must not depend on the daemon being online.

The mod's TOML reader currently accepts flat scalar values only. Any mod-side structured
TOML needs either a real parser or a build-time generator. Do not make the mod depend on
daemon TOML for built-in themes, tips, simulation behavior, or appearance data.

## Priority order

1. **Theme catalogs.** Move the records in `UI/UIScheme.cs` and `UI/TerminalTheme.cs`
   into structured theme data: IDs, labels, color roles, and terminal palettes. Preserve a
   compiled house fallback and reject incomplete or invalid palettes.
2. **Tips and small flavor catalogs.** Move `Patches/LoadingScreen.Tips.cs` to records
   with text and Grandma-mode visibility. Decide whether the single built-in
   `Useful tips` breadcrumb also becomes a shipped TOML entry. Keep attribution and the
   current filtering semantics beside the loader.
3. **Simulation tuning.** Define a versioned shipped tuning schema for worksite errands
   and plague values: RimWorld def name, work time, selection weight, bloom, run shape,
   plague dose, propagation, plant and effect limits. Keep safe defaults in code until
   validation and save-compatibility behavior are explicit; this is primarily for
   development and mod-pack tuning, not an ordinary player setting.
4. **Appearance catalogs.** Consider outfit rows, robot-face texture keys, eye-color
   weights, and excluded hair definitions. Keep the enum/asset loading and fallback logic
   in code; externalize only the lists and weights that modders may reasonably customize.
5. **Launcher profiles.** Consider TOML for expansion IDs, default game arguments, and
   sidecar launch preferences. Keep executable names, SlopWorld/Core mod identity, path
   validation, and process/lifetime safety in code.
6. **Build-time manifest cleanup.** Generate the `FileIcons.cs` lookup from
   `tools/fileicons/manifest.toml`, or validate both representations. Treat
   `tools/icons/manifest.toml` similarly where applicable.

## Explicit non-goals

- Wire routes, event/message names, enum values, and protocol limits remain in the generated
  contract and source code.
- Sandbox refusal rules, bind ordering, host escapes, credentials, and other security
  policy remain code-owned and testable at the boundary.
- UI geometry, rendering algorithms, timing guards, buffer limits, X11 details, and generated
  emoji/icon tables are implementation or generated data, not user configuration.
- Command-palette callbacks and availability predicates remain code. Only labels or groups
  should be split out if a concrete localization/customization need appears.
- Provider URLs and response-shape parsing remain provider adapters. Existing environment
  overrides are sufficient until a provider-plugin boundary is deliberately designed.

## Implementation guardrails

1. For each candidate, record whether the source is runtime-loaded TOML, compiled with
   `include_str!`, or TOML consumed by a build-time generator. Do not mix those ownership
   models accidentally.
2. Use typed records, strict unknown-field handling, validation, deterministic ordering, and
   a built-in fallback. A malformed optional file must not remove the last usable theme or
   disable a colony's simulation.
3. Keep user overrides separate from shipped data. Never put static mod catalogs in daemon
   `config.toml`, and never require the socket merely to render the mod's built-in UI.
4. Add game-free tests for parsing, fallback, ordering, invalid values, and representative
   content. For simulation data, test missing RimWorld defs and Grandma-mode selection.
5. Update the owning devnotes and this plan when a catalog changes ownership. Run `make
   format`, `make test`, and `make lint` after each migration; do not run the game or take
   screenshots as part of this review.
