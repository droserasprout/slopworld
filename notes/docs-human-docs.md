# Human documentation

The mdBook in `docs/` owns public user procedures; developer notes own implementation
constraints. README owns the project overview. `docs/src/introduction.md` includes it
through mdBook; edit README for introduction content. `tools/docs/introduction.py`
runs after native includes and adapts README links and image paths for the book.

`docs/theme/` owns book presentation and follows the palettes in `mod/Themes/`.
Screenshots use `<img align="right" width="…">` before the text they illustrate;
use `.screenshot-stack` for a related group. The shared stylesheet floats them
when the content column has room and stacks them above text on narrow layouts.
Preserve PNG originals, generate compressed JPEG copies with
`just docs-images`, and link screenshots to those JPEGs in README and the book.
Keep logos and other inline artwork outside this convention.
Cursor artwork and hotspots belong to `docs/theme/slopworld.css`; custom scrollbar
controls belong to `docs/theme/scrollbars.js`. Keep their implementation constraints
beside those owners.
Rebase `index.hbs` when upgrading mdBook and update the docs workflow's version pin
together; the override otherwise hides upstream template fixes.

Put each fact on one main page; tours and FAQ link to detailed guides and references.
Give every FAQ question an explicit stable anchor.

`SUMMARY.md` owns book navigation; page directories mirror its sections. Keep
related short topics together rather than creating a page per control.
The book includes the human-maintained root `CHANGELOG.md`; do not copy it.
Preserve the old Help troubleshooting URL through `docs/book.toml`.

Build and navigation maintenance belong to [Contributing](../docs/src/development/contributing.md#documentation).
`tools/docs/check_book.py` owns book validation and repository documentation link checks.
`tools/docs/constants.py` is the mdBook preprocessor for `{{#constant release_version}}`,
`{{#constant release_tag}}`, and `{{#constant release_tag_format}}`. It reads the
Cargo package version and shared tag conventions from `tools/version/metadata.py`;
unknown names fail the build. Use these in release examples instead of duplicating
version numbers. Build-time expansion leaves Markdown sources and the human-managed
changelog untouched. Documentation CI watches both sources of these values and the root changelog.
Public API contracts belong to the [API reference](../docs/src/reference/api.md);
wire formats and generator ownership belong to [wire protocol](protocol-wire.md).
