# Test coverage implementation plan

Scope: low-effort gaps from the September 13 coverage review. Excludes WebSocket
transport, framing, handshake, and bounded incoming-message queue findings.
Implement in order using the existing game-free test harnesses.

## 1. Preserve independent preset drafts

- Add `PresetInfoTests` and `CommandInfoTests` under `mod/Tests/` and register their
  cases in `Program.cs`; both production classes are already linked.
- Copy fully populated values and verify settings survive, including preset
  dependencies, generated-bind flags, source metadata, and command kind.
- Mutate copied lists and the preset environment dictionary; verify originals
  remain unchanged. Cover empty collections too.

Acceptance: editing a copied preset or command cannot mutate the original catalog
entry, and copying does not silently reset settings.

## 2. Complete pager lifecycle coverage

- Extend `PagerLifecycleTests` and its queued fake transport to cover release,
  close, session death, and delayed start completion.
- Verify releasing a preview preserves pinned sessions; closing a tab stops only
  its session; dead pinned tabs retire and no longer reopen.
- Complete a start callback after release or replacement and verify it cannot
  revive an obsolete preview or replace the current one. Assert obsolete session
  cleanup as well as visible ownership.
- Cover unknown/null session lookups and repeated close/release where relevant.

Acceptance: ownership and stop counts remain correct across delayed completions,
with no duplicate or orphaned preview sessions.

## 3. Exercise file mutation handlers

- Add async tests for `create_file`, `rename_file`, and `remove_file` in
  `slopd/src/api/handlers_files.rs`. Call handlers directly with `Json<FileReq>`;
  these operations need no manager, router, or game.
- Use unique temporary directories with cleanup. Verify file/folder creation,
  rename, and removal through both response status/body and filesystem state.
- Verify duplicate create/rename destinations preserve existing contents and the
  rename source. Reject invalid names, relative paths, missing parents, non-directory
  parents, and unsupported create kinds without changing the fixture.
- Test deleting a symlink to an external fixture directory preserves its target;
  include a dangling symlink. Keep every mutation inside disposable fixtures.

Acceptance: successful mutations affect only the intended entry; rejected requests
preserve existing data, and symlink removal never follows the target.

## 4. Run existing coverage in ordinary CI

- Add a push/pull-request workflow alongside `.github/workflows/release.yml`.
  Install Rust, .NET 8, and the system dependencies required by the existing suite;
  run `make test` without RimWorld assemblies.
- Install tmux/less and run `make test-pager` in CI so the existing isolated pager
  integration check is exercised regularly.
- Keep test execution separate from coverage tooling. Do not set a repository-wide
  percentage gate from the linked-source C# subset or the stale Rust report.

Acceptance: ordinary changes run Rust, C#, wire-contract, prose, and pager checks;
any failed check fails the job.

## Verification and completion

- Use `make test-mod` for C# work and `make test-daemon` for Rust work. Run
  `make format-daemon` and `make lint-daemon` after Rust edits, then `make test`
  and `make test-pager` for the completed change.
- Run `make coverage-mod` to verify the targeted paths execute. Run
  `make coverage-daemon` when `cargo-llvm-cov` and matching LLVM tools are available;
  report missing tooling rather than reusing the August 29 report as current.
- Use bounded waits for asynchronous tests. Preserve unrelated working-tree edits.
  No game launch, screenshots, or image inspection is needed.
- Update `notes/test-csharp.md` with the added coverage and remove this plan when
  complete. Report game-free validation limits; stubs do not validate Unity/Harmony.
