# Contributing

## Repository layout

`slopd/` contains the Rust daemon and host CLIs.
`mod/` contains the C# mod, assets, and tests that run without the game.
Shared build tools are in `tools/` and `just/`. The native macOS workflow
has its scripts, settings, and separate justfile in `mac/`.
User documentation is in `docs/`. Implementation notes are in `notes/`.

## Development workflow

Use the justfile for project commands. See [Build from source](../build.md) for
setup, build modes, formatting, tests, and coverage. Before you finish code changes, run the relevant tests.
Run `just lint`. It treats compiler and Clippy warnings as errors and checks formatting.

Do not use pull requests.
See the [house rules](https://github.com/droserasprout/slopworld/blob/main/notes/core-house-rules.md).

## Documentation

`just docs` builds the mdBook.
`just docs-serve` serves it locally.
Git ignores output under `docs/book/`.
Add new or moved book pages to `docs/src/SUMMARY.md`.
