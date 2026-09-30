# Rust binary review findings

Status: implemented

Review the 13 findings notes under `priv/rust-review/findings/slopd/bin` against
current source and repair the confirmed launcher, CLI, transport, and test issues.

Launcher validation now precedes profile writes, and missing profiles retain a
stable lock identity through seeding. CLI parsing shares option-value boundaries
and disambiguates injected task IDs from update notes. Transport supports worktree
rename; human output frames task text and escapes sandbox controls. Log streaming
owns bounded fan-in and cancellation without blocking child locks. HTTP fixtures
have deadlines and distinguish raw responses from Protobuf responses.

Instance handling, streaming, and HTTP fixtures have dedicated modules. Focused
ownership notes and the CLI guide describe the changed contracts. Completion
requires both binary test suites and daemon formatting/Clippy checks to pass.
