<!--- Agents, do not touch this file! It's by humans for humans ---> 

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog], and this project adheres to [Semantic Versioning].

Expect any imaginable incompatibilities until 0.1.0.

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
[Unreleased]: https://github.com/droserasprout/slopworld/compare/v0.0.2...HEAD
[0.0.2]: https://github.com/droserasprout/slopworld/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/droserasprout/slopworld/releases/tag/v0.0.1
