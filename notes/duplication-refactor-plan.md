# Duplication refactor plan

## Baseline

`jscpd` 4.0.4 found low overall duplication but several concentrated seams. The strict scan used `--min-lines 10 --min-tokens 80 --mode strict`:

- Rust: 8 clone groups, 100 duplicated lines (0.55%). A follow-up scan excluding files containing `#[cfg(test)]` left three production groups.
- C#: 24 clone groups, 337 duplicated lines (0.70%).

Repeat these scans after each phase. Do not refactor a match solely because the detector found it; preserve behavior and keep intentional test or overload symmetry out of shared abstractions.

## Work order

- [x] Extract the Rust directory timestamp helper. `jukebox::dir_stamp` and `presets::dir_stamp` duplicated the same newest-file-plus-directory-mtime logic; the shared path-taking helper now lives in [paths.rs](../slopd/src/paths.rs#L20). Keep the `None` behavior and update both manager call sites. Verify with `make test-daemon`.

- [x] Consolidate synchronous private TOML writes. `activity::save_cache` and `title::save_cache` repeated parent creation, temp-file replacement, Unix `0600` permissions, and error context ([activity.rs](../slopd/src/activity.rs#L177), [title.rs](../slopd/src/title.rs#L213)). Extract a helper that accepts serialized text; keep each cache's serialization and schema local. Reassess the analogous async writers in [endpoint.rs](../slopd/src/endpoint.rs#L57) and [config persistence](../slopd/src/config/persistence.rs#L57) separately rather than forcing sync and async APIs together.

- [x] Extract a C# daemon-config page shell. Loading state, `PageBody`/scroll setup, waiting/error text, and footer actions recur in [ConfigPage.cs](../mod/Source/SlopWorld/UI/Settings/ConfigPage.cs), [ListEditorPage.cs](../mod/Source/SlopWorld/UI/Settings/ListEditorPage.cs), [SummariesPage.cs](../mod/Source/SlopWorld/UI/Settings/SummariesPage.cs), and [UsagePage.cs](../mod/Source/SlopWorld/UI/Settings/UsagePage.cs). Introduce the small [DaemonConfigPage.cs](../mod/Source/SlopWorld/UI/Settings/DaemonConfigPage.cs) base with protected fields and save hooks. Preserve `UsagePage`'s offline-readable fields and free-text numeric editing. Verify with `make test-mod` and `make lint-mod`.

- [x] Share message-dialog rendering. [AlertDialog.cs](../mod/Source/SlopWorld/UI/AlertDialog.cs) and [ConfirmDialog.cs](../mod/Source/SlopWorld/UI/ConfirmDialog.cs) duplicated sizing, message styling, and layout. Extract the [MessageDialog.cs](../mod/Source/SlopWorld/UI/MessageDialog.cs) base, leaving alert secondary actions and confirm destructive-button policy in the concrete classes. Verify dialog sizing and all existing mod tests.

- [x] Share terminal session navigation. `HandleChrome` and `HandleKey` repeated slot switching and session walking ([TerminalInputController.cs](../mod/Source/SlopWorld/UI/TerminalInputController.cs#L120), [TerminalInputController.cs](../mod/Source/SlopWorld/UI/TerminalInputController.cs#L243)). Extract a boolean `TryHandleSessionNavigation(Event)` after each handler's distinct local/function-key gates, and preserve the different `return` behavior around offline input. Verify with `make test-mod` and `make lint-mod`.

- [x] Reassess the larger editor-shell match after the small refactors. [EditSessionDialog.cs](../mod/Source/SlopWorld/UI/EditSessionDialog.cs#L120) and [ProjectsView.cs](../mod/Source/SlopWorld/UI/ProjectsView.cs#L176) share tab rails, per-tab scrolling, and footer layout. Keep them separate: their tab sets, state models, and field/list combinations differ enough that a base would add hooks without simplifying either editor.

The post-refactor strict full-source scan reports 6 Rust clone groups / 76 duplicated lines and
15 C# clone groups / 206 duplicated lines. The remaining editor-shell match is the intentional
tabbed-editor symmetry described above.

## Leave alone unless maintenance justifies it

- Repeated Rust test fixtures in `main.rs`, `manager/caps.rs`, `git.rs`, `jukebox.rs`, and `presets.rs`; use test builders only when fixture changes become painful.
- The two `AgentSidebar` Harmony patches, which intentionally mirror two vanilla overloads.
- Files/Git tree state wrappers: `ContentTreeView` already owns the shared tree rendering and hit-testing; their fold policies differ ([FilesView.cs](../mod/Source/SlopWorld/UI/FilesView.cs#L134), [GitView.State.cs](../mod/Source/SlopWorld/UI/GitView.State.cs#L75)).
- `slopctl/logs.rs`'s sequential versus concurrent child handling and the Markdown/task selection hit-testing match; both have different ownership or line models and are lower-value abstractions.
