# Contributing

## Repository layout

`slopd/` contains the Rust daemon and host CLIs.
`mod/` contains the C# mod, assets, and tests that run without the game.
Build tools are in `tools/` and `make/`.
User documentation is in `docs/`. Implementation notes are in `notes/`.

## Development workflow

Use the Makefile for project commands. See [Build from source](../build.md) for
setup, build modes, formatting, tests, and coverage. Before you finish code changes, run the relevant tests.
Run `make lint`. It treats compiler and Clippy warnings as errors and checks formatting.

Do not use pull requests.
See the [house rules](https://github.com/droserasprout/slopworld/blob/main/notes/core-house-rules.md).

## Documentation

`make docs` builds the mdBook.
`make docs-serve` serves it locally.
Git ignores output under `docs/book/`.
Add new or moved book pages to `docs/src/SUMMARY.md`.
