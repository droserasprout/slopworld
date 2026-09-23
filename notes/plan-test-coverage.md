# Test coverage gaps

Use existing test harnesses that run without the game.
This plan excludes transport and queue work.

- Add regression tests for errand failures.
  Missing clone sources must leave no temporary project or live row.
  Host-plus-clone requests must fail before persistence.
  Host persistence failures must leave no orphaned state.
  Preserve the session-operation boundary.
- Add ordinary push/PR CI using Rust and .NET 8. Run `make test` plus isolated `make test-pager`
  with the required tmux/less dependencies. Do not require game assemblies. Keep coverage tooling separate and
  avoid a misleading whole-repo threshold from linked-source C# or stale Rust reports.

Run affected language tests through make.
Then run the full tests without the game and the pager checks through make. Use coverage
to check target execution when tools are available, not an old report. Update
[C# testing](test-csharp.md).
Delete this plan when complete.
