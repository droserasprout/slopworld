# Player pawn boundary

`Sim/Colony/PlayerPawn` owns the user-controlled pawn separately from `AgentColony`.
Session reconciliation, robot appearance, and agent work rules must not claim it.
Recovery must not create an agent session.

Dispatch accepts unmodified key-downs only when no TerminalWindow is open and
`Eco.Bare` is false. Eco rest and cutscenes suppress player input; only Eco rest
pauses creation/recovery. Bindings and rebind instructions belong to
[keyboard shortcuts](../docs/src/reference/keyboard-shortcuts.md).

The component saves its pawn reference and creates a replacement during eligible
updates when the reference is missing, destroyed, or dead. Eco state belongs to
[Eco](mod-eco.md), and cutscene coordination to [colony scenes](mod-colony-scenes.md).
