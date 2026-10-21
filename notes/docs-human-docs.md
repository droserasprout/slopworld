# Human documentation

The mdBook in `docs/` owns user procedures; notes own implementation constraints.
README owns the overview and is included by `docs/src/introduction.md`.
`tools/docs/introduction.py` adapts its links/images after native includes.

Put each fact on one page; tours and FAQ link to it. FAQ questions need stable
anchors. `SUMMARY.md` owns navigation, with directories matching its sections.
Keep short related topics together. Include the root changelog rather than copying it,
and preserve the old Help troubleshooting URL through `docs/book.toml`.

`docs/theme/` owns presentation and follows `mod/Themes/` palettes. Screenshots use
right-aligned images before their text; `.screenshot-stack` groups them. Preserve PNG
originals, generate JPEGs with `just docs-images`, and reference those copies. Logos
and inline artwork use their own layout. Keep cursor/scrollbar implementation details
beside their stylesheet/script owners. Rebase `index.hbs` with mdBook upgrades and
update the workflow pin together, or the override will hide upstream template fixes.

`tools/docs/check_book.py` validates navigation and repository links.
`tools/docs/constants.py` expands release examples from shared version metadata;
use its constants instead of copied versions. Expansion must leave Markdown sources
and the human-managed changelog untouched. CI watches both metadata and changelog.

See [Contributing](../docs/src/development/contributing.md#documentation) for maintenance,
[API reference](../docs/src/reference/api.md) for public contracts, and
[wire protocol](protocol-wire.md) for generator ownership.
