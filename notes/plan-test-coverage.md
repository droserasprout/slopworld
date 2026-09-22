# Test coverage gaps

Use existing game-free harnesses; this plan excludes transport/queue work.

- Add errand failure regressions: missing clone sources must leave no temporary project or
  live row, host-plus-clone requests must fail before persistence, and host persistence
  failures must leave no orphaned state. Preserve the session-operation boundary.
- Add ordinary push/PR CI using Rust and .NET 8. Run `make test` plus isolated `make test-pager`
  with required tmux/less dependencies; no game assemblies. Keep coverage tooling separate and
  avoid a misleading whole-repo threshold from linked-source C# or stale Rust reports.

Run affected language tests, then full game-free tests/pager checks through make. Use coverage
to confirm target execution when tools are available, not an old report. Update
[C# testing](test-csharp.md) and delete this plan when complete.
