<!--- Agents, do not touch this file! It's by humans for humans ---> 

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog], and this project adheres to [Semantic Versioning].

Expect any imaginable incompatibilities until 0.1.0.

## [Unreleased]

### Added

- Terminal: Added "Save as" menu to create prompt, breadcrumb, shell script, or file action from selected text.

### Fixed

- UI: Fixed syntax highlighting in diffs breaking after long lines.

## [0.0.10] - 2026-10-23

### Security

- Sandbox: Prevent crafted terminal input from executing tmux commands outside the sandbox.
- Sandbox: Prevent crafted `.git` files from granting write access to another repository's metadata.

### Added

- CLI: Added `slopctl config get` and `slopctl config set` to read and change daemon settings.
- CLI: Added `slopworld connect` and `slopworld endpoints` to manage saved daemon connections.
- CLI: Added automatic game profile selection for each connection.
- Settings: Added configurable on-exit actions to the General tab.
- UI: Added missing View and Add actions to Command palette.

### Fixed

- Apps: Recognize Debian’s `batcat` executable.
- Game: Mute ambient sounds while the colony is paused.
- Game: Player pawn avoids construction job.
- Jukebox: Fall back to local OST playback when daemon playback fails.
- Jukebox: Drop the jukebox in a pod during initial cutscene.
- Jukebox: Remove random pauses between local OST tracks.
- Settings: Refresh active fonts and font menus after rescanning installed fonts.
- Terminal: Fixed image paste failing when the daemon starts before the graphical session.
- Terminal: Fixed image paste into Codex on remote daemon.
- Terminal: On Shift+Enter send Enter in shells and multiline input in agents.
- Terminal: Prevent stalled clipboard helpers from blocking later copy-paste actions.
- Terminal: Preserve control characters in keyboard input and clipboard paste on tmux 3.8.
- Terminal: Use the game desktop's clipboard when connected to remote daemon.
- UI: Fixed separator lines disappearing during pixel snapping.
- UI: Show the daemon hostname in the game window title.
- UI: Show an empty state in the Agents sidebar.

### Changed

- Packaging: Require bat, less, micro, xclip, and tmux 3.7+.
- Settings: Adjust default audio volumes and backdrop dimming.
- Storage: Store connection files in `endpoints/`.
- Terminal: Keep clipboard access on the game desktop, including remote connections.
- UI: Prefer installed Nerd Font variants when terminal font is set to Auto.
- UI: Updated default fonts and sizes.

### Removed

- API: Removed `/clipboard` methods and image imports from file URIs.
- Clipboard: Removed unused `wl-clipboard` integration.

## [0.0.9] - 2026-10-21

### Fixed

- Agents: Preserve temporary agents’ settings and private-state identity across daemon restarts.
- Keyboard: Restore the last active session or open file when switching between groups.
- Library: Fixed project renames breaking saved library items.
- Projects: Prevent deleting projects referenced by saved library items.
- Sandbox: Copy user Codex skills into new private directories.
- Sandbox: Fixed skipped files being copied into private directories.
- Sandbox: Fixed command comparison in live inspection.
- Sandbox: Fixed user-installed .NET SDKs being hidden by private CLI state.
- Sandbox: Mount custom Kubernetes configuration files listed in KUBECONFIG.
- Sandbox: Preserve X11 and other preset sockets when using persistent /tmp.
- Sandbox: Preserve captured cache paths and warning levels across edits and daemon restarts.
- Sandbox: Reject overlapping private directories.
- UI: Fixed unchecking All in agent status, Library, and task filters.

### Changed

- Agents: Require stopping running agents before deleting them.
- Settings: Made post-processing intensity configurable.
- UI: Use grained texture for accented surfaces.

## [0.0.8] - 2026-10-20

### Added

- Settings: Added "CRT overlay" toggle on Display tab to apply post-processsing effects.

### Fixed

- Commands: Command parsing now recognizes shell comments. 
- Commands: Reject commands with unfinished quotes or trailing escapes.
- Files: Fixed file actions misinterpreting placeholder text in filenames.
- Files: Fixed ignored entries hiding visible files in directories with more than 500 entries.
- Files: Fixed handling filenames with trailing spaces and environment variable references.
- Files: Hide git shortstat from foldable groups.
- Files/Git/Search: Reject non-UTF-8 filenames to prevent opening or modifying the wrong file.
- Git: Fixed diffs showing renamed files as additions when the original path is outside the selected directory.
- Git: Fixed handling special characters in filenames.
- Git: Fixed unstaging renamed files leaving the original path's deletion staged.
- Jukebox: Fixed playback failing when the preferred audio device is unavailable.
- Jukebox: Report when station changes are saved but invalid definitions prevent the live catalog from reloading.
- Library: Fixed command placeholders becoming part of the entered command.
- Library: Prevent duplicate saves and edits while saving.
- Search: Fixed blank result previews for lines containing non-UTF-8 text.
- Search: Fixed personal ripgrep settings affecting matching files.
- Search: Preserve leading and trailing spaces, including whitespace-only queries.
- Settings: Fixed unsaved sandbox and app preset edits affecting the shared catalog.
- Settings: Preserve trailing spaces in sandbox environment variable values.
- Tasks: Keep available recipients up to date while the delegation dialog is open.
- Terminal: Fixed opening a terminal path link on correct line number when the file is already open.
- Terminal: Fixed terminal path links failing in directories with more than 500 entries.
- UI: Allow retrying failed temporary project path previews.
- UI: Fixed unchecking All projects and All worktrees in the project filter.
- UI: Fixed saving temporary projects opened from the Worktrees tab.
- UI: Preserve directory picker navigation after a failed directory read.
- UI: Tuned loading screen animation.
- Usage: Fixed polling countdown intervals.
- Workers: Prevent older base revision previews from overwriting newer results.

### Changed

- Settings: Select and highlight Storage rows on click, add "Explore" button.

## [0.0.7] - 2026-10-19

### Added

- UI: Added an Open files toggle to the sidebar (button before Project filter or F9 shortcut).

### Fixed

- Keyboard: Fixed Alt navigation shortcuts consumed by the focused Search field.
- Keyboard: Skip file panes when cycling sessions with Alt+Z/Alt+X.
- Terminal: Fixed selection drift in scrollback when new output arrives.
- UI: Complete context menus for open file tabs.
- UI: Fixed missing Git shortstat on checkout's foldable heading in sidebar.
- UI: Fixed active markdown preview tabs not being highlighted in sidebar.
- UI: Fixed dropdown spacing, hover highlights, and closing on second click.
- UI: Fixed flicker when switching between two files in unpinned tab.
- UI: Fixed Search preview tabs pinning.
- UI: Fixed slow sidebar resizing with Settings open.
- UI: Fixed status bar button highlights and spacing.
- UI: Pressing Search now preserves the open file's terminal pane.
- UI: Tabs and navigation now respond on mouse press instead of release.
- UI: Unified dropdown selectors' styling and behavior.
- UI: Use distinct icons for markdown and image preview tabs.

### Changed

- Keyboard: Preserve active sidebar tab when pressing F12 or Alt+A/Alt+S.
- Settings: Separate VSync from the frame limiter and disable it by default.
- Settings: Default the frame limiter to Auto, matching the display refresh rate.
- UI: Improve titles in the status bar, share context menus with sidebar items.
- UI: Show Open files across all sidebar tabs.

## [0.0.6] - 2026-10-18

### Added

- Keyboard: Added Alt+A/Alt+S shortcuts to focus previous/next open file.
- Keyboard: Added Shift+Alt passthrough for navigation shortcuts to send them without Shift.
- UI: Added folding for project/checkout/file results on Search sidebar tab.
- UI: Mark main checkouts with a root icon on sidebar tabs.

### Fixed

- Sandbox: Retain numeric UID/GID in Bubblewrap to allow passwd-based home lookups.
- Search: Apply search options immediately when toggled.
- Search: Keep results independent of Files visibility settings.
- Tasks: Preserve and display the full history of progress reports and status updates.
- UI: Fixed loading screen brightness jump.
- UI: Don't add nested foldable in sidebar trees when only the main checkout is selected.
- UI: Small fixes in scrollbar widget and Sidebar layout.

### Changed

- Search: Increase the result limit from 200 to 1,000 matching lines per checkout.
- UI: Fade wide sidebar labels to bg color instead of truncating with ellipsis.
- UI: Show checkout branches and Git change summaries in shared tree headings.

## [0.0.5] - 2026-10-17

### Added

- Library: Added built-in Magic 8 ball breadcrumb for making important decisions.
- Search: Added include/exclude filters.

### Fixed

- Jukebox: Fixed stream idle timeout regression after ureq upgrade.
- Sandbox: Fixed missing bind paths in rust-cache preset.
- Terminal: Hide built-in breadcrumbs when Grandma's visiting.
- UI: Hide usage indicators in statusbar if API keys are not set.

### Changed

- Settings: Set default Usage polling interval to 120s and disable by default.

## [0.0.4] - 2026-10-15

### Fixed

- Jukebox: Recover audio playback after ALSA/PipeWire output xrun failures.
- Sessions: Fixed deadlock on saving agent with workspace switch.
- Workspace: Fixed worktree filtering and project menu navigation.

## [0.0.3] - 2026-10-14

### Added

- Sandbox: Added `slopworld-dev` preset for building and running tests with read-only game files.
- Settings: Allow editing sandbox preset warning levels and explanations.

### Fixed

- CLI: Honor custom `MODS` paths when installing or removing the mod.
- Game: Keep cleared plague fields from returning after loading a save.
- Game: Resume unfinished opening scenes under Eco after loading a save.
- Markdown: Display images inside `div` containers, stack grouped images vertically.
- Sandbox: Reject preset path aliases that expose host originals instead of private copies.
- Sessions: Complete stop/restart cleanup even if the requesting client disconnects.
- Sessions: Prevent recovery from attaching surviving host terminals or workers to a different agent.
- Sidecar: Support non-default Linux user and group IDs, including SSH account lookup.
- Terminal: Fixed mouse passthrough in running terminal applications after game restart.
- Terminal: Correctly repaint wrapped links when terminal output changes.
- Terminal: Prevent scrollback corruption when history reaches its limit.
- Terminal: Update terminal dimensions after resizing even when the application produces no output.
- UI: Correct sidebar click handling and resizing when docked on the right.
- UI: Fixed some agent action buttons remaining clickable but showing no hover highlight or tooltip.
- UI: Prevent stale project and Library settings from reappearing after edits.
- Workers: Prevent hangs when creating workers in new worktrees and clean up abandoned worktrees after cancelled launches.
- Workers: Restore workers' own launch settings after daemon restarts.

### Changed

- Sidecar: Configure user and group IDs through `PUID`/`PGID`. Container starts as root, then drops privileges.
- Sandbox: Distinguish sensitive access from host control and show warnings inherited through preset dependencies.

### Security

- CLI: Refuse HTTP redirects to prevent authentication tokens from reaching another endpoint.
- Daemon: Reject blank root tokens and generate a secure token on first startup.
- Daemon: Reject pending mutations authenticated with an old root token after token rotation.

## [0.0.2] - 2026-10-12

### Added

- Packaging: Added Fedora RPM packages.
- Settings: Added configurable date format (General tab).

### Fixed

- Files: Fixed workspace viewers showing internal tab names instead of filenames.
- Markdown: Keep preview visible while loading replacement.
- Packaging: Fixed release archive permissions under restrictive umasks.
- Tasks: Fixed double-, triple-click selection, and copying to PRIMARY in task details.
- Tasks: Respect the configured time format in task details.
- Terminal: Preserve zsh host shell tab titles between restarts.
- UI: Fixed hover highlights jumping after keyboard input in scrollable views.
- UI: Fixed a character jumping between lines in tooltips when moving the pointer.
- UI: Replaced oversized Search checkboxes with compact text toggles.

### Changed

- Code: Default bat highlighting to ansi, following the terminal palette.
- Sidecar: Don't install agent CLIs in base image.
- UI: Include newly added projects and worktrees in the sidebar filter.

### Other

- Music: Updated soundtrack exports.

## [0.0.1] - 2026-10-10

Initial release 🐈

<!-- Links -->
[keep a changelog]: https://keepachangelog.com/en/1.0.0/
[semantic versioning]: https://semver.org/spec/v2.0.0.html

<!-- Versions -->
[Unreleased]: https://github.com/droserasprout/slopworld/compare/v0.0.10...HEAD
[0.0.10]: https://github.com/droserasprout/slopworld/compare/v0.0.9...v0.0.10
[0.0.9]: https://github.com/droserasprout/slopworld/compare/v0.0.8...v0.0.9
[0.0.8]: https://github.com/droserasprout/slopworld/compare/v0.0.7...v0.0.8
[0.0.7]: https://github.com/droserasprout/slopworld/compare/v0.0.6...v0.0.7
[0.0.6]: https://github.com/droserasprout/slopworld/compare/v0.0.5...v0.0.6
[0.0.5]: https://github.com/droserasprout/slopworld/compare/v0.0.4...v0.0.5
[0.0.4]: https://github.com/droserasprout/slopworld/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/droserasprout/slopworld/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/droserasprout/slopworld/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/droserasprout/slopworld/releases/tag/v0.0.1
