# Agent pawn patches

`mod/Source/SlopWorld/Patches/Agents/` owns pawn protection and interaction overrides;
[mod sources](mod-source-layout.md) map neighboring integrations.

Agents use `NameSingle` and are excluded from relation generation rather than given
fabricated relatives. Stopping a session normally marks its retained pawn offline/downed;
it does not retire or replace that pawn. Reconciliation can repair injuries from old
saves. Agents must not become rescue/strip targets merely because their process stopped.

Protection covers direct pawn damage and animal targeting. Fire attachment and fire
damage protection applies to every player-faction pawn, including untracked colonists.
Construction eligibility/success belongs to [Worksite](mod-worksite.md), sidebar
geometry to [sidebar](mod-sidebar.md), lifecycle to [simulation](mod-sim.md), and
session/gizmo input to [terminal](mod-terminal.md).
