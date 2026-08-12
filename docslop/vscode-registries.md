# How VS Code wires actions, keys and settings

Read before touching `CommandPalette`, `KeyBindingsPage` or the F-key gate. Line
numbers are `microsoft/vscode` at `690978d` (2026-08-12), under `src/vs/`; the
installed copy in `/usr/share/code` is bundled and cannot be read for this.

There is no "actions system". There are **four registries of plain data**, none of
which knows about the others, and one function that writes to all of them.

| Registry | File | Holds |
|---|---|---|
| `CommandsRegistry` | `platform/commands/common/commands.ts:67` | id → handler, nothing else |
| `KeybindingsRegistry` | `platform/keybinding/common/keybindingsRegistry.ts:87` | chord → command id, `when`, weight |
| `MenuRegistry` | `platform/actions/common/actions.ts:486` | MenuId → command id, `when`, group, order |
| `ConfigurationRegistry` | `platform/configuration/common/configurationRegistry.ts` | setting id → JSON schema, scope |

A command is `{id, handler}`. Title, icon, category and availability live in the
*other* registries and point back by string. That is the whole separation.

## `registerAction2` is fifty lines

`actions.ts:726` takes one declaration and splits it four ways: handler to the
commands registry, `menu` to the menu registry, `keybinding` to the keybindings
registry, and `f1: true` to `MenuId.CommandPalette` - **the palette is a menu**
(`:753`), not a special road. It hands back one disposable that undoes all four.

The only real logic in the fan-out is `:763`: **`precondition` is ANDed into the
keybinding's `when`.** Unavailable is said once and means greyed in the menu,
absent from the palette, and the key falling through to whoever is behind it.

## Context keys are what make it one system

`ContextKeyExpr` (`platform/contextkey/common/contextkey.ts`) is a small boolean
language over a scoped key/value store, serialisable both ways. It is the same
type under the same field name - `when` - on a keybinding, a menu item and a
precondition. Sharing that one type is the trick; the rest is bookkeeping.

Settings reach it for free: `ConfigAwareContextValuesContainer`
(`platform/contextkey/browser/contextKeyService.ts:105`) exposes **every setting
as a context key under `config.`** (`:127`, `:161`), invalidated on change. No
per-setting wiring, so `when: "config.editor.minimap.enabled"` simply works.

## Resolution is last-wins

`platform/keybinding/common/keybindingResolver.ts`:

- Defaults then user overrides into one array (`:71`), scanned **backwards**
  (`:381`). Priority is array position and `when`, nothing else.
- `weight` (`keybindingsRegistry.ts:62`, EditorCore 0 → ExternalExtension 400) is
  only the **sort key** (`:261`). It is not consulted at dispatch.
- `handleRemovals` (`:124`) is the `"-command.id"` syntax. It matches by
  *implication*, not equality (`:113`), so a user's removal survives the shipped
  `when` being narrowed in a later release.
- `resolve` answers three ways, not two: `NoMatchingKb`, `MoreChordsNeeded`,
  `KbFound` (`:22`). The middle one is what a chord prefix needs, and what a pane
  that forwards keys needs in order to hold one back.

## The terminal, which is our problem exactly

`workbench/contrib/terminal/browser/terminalInstance.ts:1143` is one key handler
that asks **`softDispatch`** - resolve, do not execute
(`platform/keybinding/common/abstractKeybindingService.ts:149`) - and then:
a chord prefix goes to the workbench if `allowChords`, Escape excepted (`:1155`);
a found command goes to the workbench if it is on the skip list (`:1167`);
everything else goes to the shell.

The skip list is a setting, `terminal.integrated.commandsToSkipShell`: a shipped
default set that the user's array *edits*, `-` removing an entry, the same
convention as keybindings (`terminalConfigurationService.ts:64`). It names
**command ids, not keycodes**, so it stays right after a rebind.
`sendKeybindingsToShell` flips the default wholesale.

## What this costs us

Ours are three unrelated mechanisms with no shared vocabulary:

- **Commands.** No registry. `UI/CommandPalette.cs` builds a `List<Entry>` by hand
  (`:57`) and is the only thing that can invoke one.
- **Keys.** `KeyBindingDef` - one `KeyCode`, no modifiers, no `when` - drawn by
  `UI/KeyBindingsPage.cs`, plus hardcoded handling in `Sim/TerminalHotkeys.cs`,
  `TerminalWindow.HandleKey`/`HandleFunctionKey`, `Patches/StripKeys.cs` and
  `Patches/ShortcutKeysPatch.cs`. The F-key gate (`TerminalHotkeys.cs:34`) is
  `commandsToSkipShell` written as an `if`.
- **Settings.** Four places a knob, by [mod-settings](mod-settings.md)'s own count.
- **Context keys.** None. Every availability question is an `if` at the call site.

The borrow worth making is **context keys and a command-id registry**, in that
order. They are what collapse the other three; `registerAction2` is glue over
them, and `softDispatch` - can the chrome claim this key, asked without firing it -
cannot be written without them.
