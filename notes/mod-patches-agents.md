# Agent patches

Start in `Patches/Agents/` for pawn protection and work overrides; colonist-bar geometry
belongs to `Patches/ColonistBar/` and [sidebar integration](mod-sidebar.md).

Agent names use `NameSingle`; vanilla parent-name logic casts to `NameTriple`. Suppress
relation generation rather than fabricating relatives. Virtual relation targets require
manual patch registration.

Disabled-work lists are pawn-owned caches, so Construction eligibility must survive cache
rebuilds. Construction success is enforced through the stat part: patching only a job path
misses vanilla's per-tick failure roll, which can erase materials and progress.

A down agent is a stopped process, not an injured colonist to rescue, strip or replace.
Protection must cover attachment and cell damage paths as well as direct pawn damage.
Session cycling and gizmo redraws must respect the terminal's input/layer ownership.
