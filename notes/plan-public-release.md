# Public alpha readiness

This plan proposes requirements for an initial Linux release. It does not certify a release.
Before tagging:

- Specify tested architecture/game/DLC/agent requirements and GOG versus Steam status.
- Choose source installation or a complete distributable: current release workflow omits
  the mod. Verify fresh-user install/auth/first-task without devnotes.
- Supply project licensing and actual distribution notices.
  [Attribution](core-attribution.md) is guidance, not a substitute for supplied licenses.
- Require CI verification for tests without the game, lint, docs, and generated changes.
  Separately check the mod build against game assemblies. [Docs gaps](plan-docs-review.md) cover onboarding and support.
- Test restart/update/backup recovery and document breaking changes and remaining limits.
  Do not claim macOS parity before [platform validation](ops-macos-compatibility-status.md).

Record results for the release revision. Runtime game checks require an explicit request.
