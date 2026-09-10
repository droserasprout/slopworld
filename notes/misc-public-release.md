# Public release readiness

Static review: 2026-09-10. Suggested scope: a Linux-first public alpha for
RimWorld 1.6. This is a proposed cutoff, not a release certification; tests and
runtime checks were not run for this review. Existing bug plans need confirmation
against current code.

## Feature cutoff

Freeze new features now. Accept release work for installation, incorrect behavior,
hangs, data loss, resource growth, and essential documentation. Defer macOS parity,
new integrations, UI redesigns, extra jukebox features, and broad refactoring.
Optional features with unresolved serious bugs can be disabled for the alpha.

## Suggested actions, in order

1. **Set the support contract.** Name the tested Linux distro/architecture, RimWorld
   1.6 version, DLC requirements, and supported agent CLIs. Verify Steam separately
   from the documented GOG setup. Label macOS experimental until the checks in
   [macOS status](ops-macos-compatibility-status.md) pass.
2. **Choose the installation deliverable.** The current
   [release workflow](../.github/workflows/release.yml) ships daemon binaries, a
   service, and a desktop entry; it does not ship the mod. Either complete the
   package or explicitly release a tagged source installation. Test installation
   with fresh user state and document every prerequisite.
3. **Resolve reliability findings.** Reproduce and fix the
   [texture leaks and unbounded queues](_plans/memory-leaks-plan.md),
   [blocked audio controls](plan-jukebox-bugfix.md), and
   [experimental feature gating](plan-review-experimental-features.md).
   Prioritize hangs, memory growth, and unexpected prompt injection; defer minor
   optional-feature defects only with an explicit disposition.
4. **Complete licensing and notices.** Choose a top-level project license and
   assemble the distribution notices for dependencies, fonts, artwork, and music.
   The [attribution note](core-attribution.md) references `THIRD_PARTY_LICENSES`,
   but the review found neither that file nor a top-level license. Check notices
   in the actual release contents.
5. **Add release gates.** Extend CI beyond daemon tests to game-free C# tests,
   linting, documentation, and generated-contract drift checks. Verify the mod
   build separately against real game assemblies. Use `make test`, `make lint`,
   `make docs`, and focused generated-output checks through Makefile targets.
6. **Finish first-run documentation.** Cover agent CLI installation/authentication,
   service health, the first project and agent, and a successful first task. Use
   the [docs review plan](_plans/docs-review-plan.md); prioritize onboarding and
   the support/distribution matrix over expanding the API reference.
7. **Define upgrades and recovery.** State the alpha compatibility policy and
   document breaking changes per release. Test backup restoration and updating
   daemon and mod together. Replace the empty [changelog](../CHANGELOG.md) entries
   with release scope, installation, upgrade instructions, and known limitations.

## Release acceptance

- A new user follows only published instructions to install, authenticate an
  agent, create a project, and complete a task.
- Agent, daemon, and machine restarts preserve the documented state; update,
  uninstall, and backup recovery behave as documented.
- Sustained terminal output and reconnects keep memory bounded and input
  responsive; shutdown does not hang. Optional audio cannot block its controls.
- The exact release revision passes automated checks and the supported-platform
  smoke test. Record results and unresolved limitations before tagging.

Game-based smoke tests remain outstanding and require an explicit request under
the repository rules. No game launch or image inspection is implied by this note.
