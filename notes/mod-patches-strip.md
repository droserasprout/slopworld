# Stripping vanilla systems

Strip patches disable selected vanilla simulation, interaction, and UI behavior
while retaining definitions needed by lookup, persistence, or other readers.
Do not delete a def merely because its visible control is hidden.

Hidden buttons and keys remain callable elsewhere. Filter vanilla activation and
key-read paths as well as drawing and the binding editor, preserving intentional
mod-owned key readers. Vanilla implied bindings and screenshot handling can consume
input before the terminal. Hidden vanilla input remains filtered after resetting
bindings; reset sanitization separately clears conflicting WASD camera-dolly assignments.

Settings category layout belongs to [dialogs](mod-ui-windows.md). The two Options
hosts and GUI-group lifetime also belong to that dialog owner.
