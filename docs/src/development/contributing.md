# Contributing

## Repository layout

`slopd/` contains the Rust daemon and host CLIs.
`mod/` contains the C# mod, assets, and tests that run without the game.
Shared Python tools form the `tools` package, grouped by responsibility with tests
beside their owners. `just/` owns their public recipe entry points. The native macOS workflow
has its scripts, settings, and separate justfile in `mac/`.
User documentation is in `docs/`. Implementation notes are in `notes/`.

## Development workflow

Use the justfile for project commands. See [Build from source](build.md) for
setup, build modes, formatting, tests, and coverage. Before you finish code changes, run the relevant tests.
Run `just lint`. It treats compiler and Clippy warnings as errors and checks formatting.

Do not use pull requests.
See the [house rules](https://github.com/droserasprout/slopworld/blob/main/notes/core-house-rules.md).

## Documentation

`just docs` builds the mdBook and checks local links, rendered anchors, and that
every book page appears once in the summary. The documentation publication workflow
runs the same checks. It also checks local file and directory links in repository
READMEs and `notes/` Markdown, including new files that Git does not ignore.
Repository link checks validate target existence; anchor checks apply to the rendered
book. `just test-docs` tests the checker without building the book.
`just docs-serve` serves it locally.
Both recipes generate the book introduction from the root README before starting.
Edit `README.md` to change the introduction; `just refresh-introduction` regenerates
it separately, including while a documentation server is already running.
Git ignores output under `docs/book/`.
Add new or moved book pages to `docs/src/SUMMARY.md`.
Use repository URLs for files outside the book: relative links to source or notes
will not exist on the published site. Preserve incoming anchors when moving sections.

Keep user procedures in the book and implementation constraints in `notes/`.
Tours and FAQ should link to the guide that owns each procedure. Edit generated
API inventories through their generator rather than changing the output directly.
