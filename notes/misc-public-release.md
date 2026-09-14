# Public alpha readiness

Proposed Linux-first cutoff, not a release certification. Before tagging:

- Specify tested architecture/game/DLC/agent requirements and GOG versus Steam status.
- Choose source installation or a complete distributable: current release workflow omits
  the mod. Verify fresh-user install/auth/first-task without devnotes.
- Resolve or explicitly disable blockers in [audio](plan-jukebox-bugfix.md) and
  [experimental discovery](plan-review-experimental-features.md).
- Supply project licensing and actual distribution notices; [attribution](core-attribution.md)
  is guidance, not a substitute for shipped licenses.
- Gate game-free tests, lint, docs and generated drift in CI; separately verify mod build
  against game assemblies. [Docs gaps](plan-docs-review.md) cover onboarding and support.
- Test restart/update/backup recovery and document breaking changes and remaining limits.
  Do not claim macOS parity before [platform validation](ops-macos-compatibility-status.md).

Record results for the release revision. Runtime game checks require an explicit request.
