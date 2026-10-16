# Contributing

## Repository layout

`slopd/` contains the Rust daemon and host CLIs.
`mod/` contains the C# mod, assets, and tests that run without the game.
Shared Python tools form the `tools` package, grouped by responsibility with tests
beside their owners. The root justfile exposes flat, prefixed recipes and aggregates; `just/` owns
the component recipes and specialist menus. `mac/` owns native macOS scripts and
settings, exposed through `mac-*` recipes.
User documentation is in `docs/`. Implementation notes are in `notes/`.

## Development workflow

Use the justfile for project commands. See [Build from source](build.md) for
setup, build modes, formatting, tests, and coverage. Before you finish code changes, run the relevant tests.
Run `just lint` to check formatting, static analysis and Clippy warnings.
Run `just mod-check-build` to check C# compiler warnings against game assemblies.

Do not use pull requests.
See the [house rules](https://github.com/droserasprout/slopworld/blob/main/notes/core-house-rules.md).

## Documentation

`just docs-build` builds the mdBook and checks local links, rendered anchors, and that
every book page appears once in the summary. The documentation publication workflow
runs the same checks. It also checks local file and directory links in repository
READMEs and `notes/` Markdown, including new files that Git does not ignore.
Repository link checks validate target existence; anchor checks apply to the rendered
book. `just tools-test tools/docs` tests the checker without building the book.
`just docs-serve` serves it locally.
The Introduction includes `README.md` during the book build. Edit README directly;
the book adapts its links and image paths without generating a source copy.
Git ignores output under `docs/book/`.
For manually captured screenshots, run `just docs-images` to create heavily
compressed `.jpg` copies of every PNG under `docs/src/`. The default JPEG quality
is 15; use `just docs-images 10` for stronger compression. Dimensions stay
unchanged, transparency becomes white, and original PNGs are preserved. Link the
desired JPEGs from the book explicitly; the recipe does not rewrite links.
Conversion is an explicit preparation step, separate from `just docs-build`.
Add new or moved book pages to `docs/src/SUMMARY.md`.
Use repository URLs for files outside the book: relative links to source or notes
will not exist on the published site. Preserve incoming anchors when moving sections.

Keep user procedures in the book and implementation constraints in `notes/`.
Tours and FAQ should link to the guide that owns each procedure. Edit generated
API inventories through their generator rather than changing the output directly.
