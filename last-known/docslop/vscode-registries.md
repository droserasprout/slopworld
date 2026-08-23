# VS Code registry notes

Reference snapshot: `microsoft/vscode` `690978d` (2026-08-12). Read this before
changing `CommandPalette`, `KeyBindingsPage` or the F-key gate.

VS Code has four independent registries:

| Registry | Stores |
| --- | --- |
| `CommandsRegistry` | command id → handler |
| `KeybindingsRegistry` | chord → command id, `when`, weight |
| `MenuRegistry` | menu → command id, `when`, group, order |
| `ConfigurationRegistry` | setting id → JSON schema and scope |

`registerAction2` fans one declaration into those registries and returns one
disposable. `f1: true` adds a normal menu item to the Command Palette. `precondition`
is combined with a keybinding's `when`, so one availability predicate controls menu
visibility, palette presence and key fall-through. Context keys are a small boolean
language shared by those `when` fields; settings are exposed automatically as
`config.*` context keys.

Keybindings are resolved from the combined defaults/user array backwards. `weight`
sorts the array but does not win dispatch. Removals use `-command.id` and implication
matching. Resolution distinguishes no match, chord prefix (`MoreChordsNeeded`) and
found binding. The terminal first soft-dispatches workbench commands: allowed chords
and skip-listed commands stay in the workbench, everything else reaches the shell.
The skip list contains command ids, not keycodes; `sendKeybindingsToShell` flips the
default.

SlopWorld currently has no equivalent shared system: commands are a hand-built list,
keys are `KeyBindingDef` plus hardcoded terminal/map gates, settings are split across
several stores, and availability is checked at call sites. If this is unified, add a
command-id registry first, then context keys, then use them for palette, key dispatch
and settings visibility. Do not copy VS Code's registry fan-out without those two
shared concepts.
