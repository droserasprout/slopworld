# Contributing

## Repository layout

`slopd/` contains the Rust daemon and host CLIs; `mod/` contains the C# mod,
assets, and game-free tests. Build tooling lives in `tools/` and `make/`.
User documentation lives in `docs/`, implementation notes in `notes/`.

## Development workflow

Use the Makefile for project commands. See [Build from source](../build.md) for
setup, build modes, formatting, tests, and coverage. Before finishing code changes,
run the relevant tests and `make lint`; it treats compiler and clippy warnings as
errors and verifies formatting.

All work lands on `main`. Branches are used only for work that will be merged back;
see the [house rules](https://github.com/droserasprout/slopworld/blob/main/notes/house-rules.md).

## Generated output

Edit `protocol/wire.yaml` and run `make api-contract` to update shared wire bindings.
`make api-docs` generates the API route inventory from the router;
`make reference` generates the developer environment/API/CLI reference.
Asset-generation targets are listed in `make` help; their implementations live in
`tools/`.

## Documentation

`make docs` builds the mdBook; `make docs-serve` serves it locally. Output under
`docs/book/` is ignored. Run `make lint-prose` on prose changes.

Devnotes preserve cross-file architecture and constraints that code cannot show.
Use the notes in the repository's `notes/` directory to find a topic and the
[prose guide](https://github.com/droserasprout/slopworld/blob/main/notes/prose-guide.md)
for writing rules. Published docs take precedence over devnotes.
