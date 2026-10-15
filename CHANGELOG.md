<!--- Agents, do not touch this file! It's by humans for humans ---> 

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog], and this project adheres to [Semantic Versioning].

Expect any imaginable incompatibilities until 0.1.0.

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
- Workers: Restore workers’ own launch settings after daemon restarts.

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
[Unreleased]: https://github.com/droserasprout/slopworld/compare/v0.0.4...HEAD
[0.0.4]: https://github.com/droserasprout/slopworld/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/droserasprout/slopworld/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/droserasprout/slopworld/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/droserasprout/slopworld/releases/tag/v0.0.1
