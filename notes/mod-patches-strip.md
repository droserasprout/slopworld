# Stripping vanilla systems

`Patches/` suppresses simulation and UI entry points while retaining RimWorld definitions.
Keep definitions that lookup and persistence code uses. Do not delete a def merely because its control is hidden.

Hidden buttons/keys remain callable elsewhere. Gate activation and every key read path as
well as drawing and the binding editor. Vanilla implied bindings and screenshot handling
can consume input before the terminal. Restoring defaults must not revive stripped keys.

Options category defs stay registered even when hidden. Merge/reposition their UI without
breaking `AllDefs`, `GetNamed`, or vanilla's fixed-coordinate layout. GUI groups need cleanup after exceptions.
The embedded Options view and standalone main-menu dialog use different hosts.
See [content views](mod-content-views.md) and [Settings](ui-settings.md).
