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

## Generated output

Edit the stable wire definition in `shared/protocol.yaml`.
Run `make api-contract` to update the Rust and C# bindings.

Rust code defines the read models for daemon defaults, prompts, and usage policy.
Change that Rust code and its API tests.
Do not add generated client constants for these models.
`make api-docs` generates the API route inventory from the router.
`make reference` generates the developer environment/API/CLI reference.
`make` help lists targets that generate assets. Their implementations are in `tools/`.

## Documentation

`make docs` builds the mdBook.
`make docs-serve` serves it locally.
Git ignores output under `docs/book/`.
Add new or moved book pages to `docs/src/SUMMARY.md`.

Developer notes describe relationships between files and constraints that code cannot show.
Use the notes in the repository's `notes/` directory to find a topic and the
[developer-note writing guide](https://github.com/droserasprout/slopworld/blob/main/notes/docs-prose-guide.md)
for writing rules. The book owns public user procedures; verify behavior claims
against source and tests when resolving conflicts with developer notes.

Rust test bodies live beside their owner in separate test modules. Binary-root tests
use the binary's subdirectory because standalone `src/bin/` files become executables.
Keep standalone fixtures in excluded test files.
