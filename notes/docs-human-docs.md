# Human documentation

The mdBook in `docs/` owns public user procedures; developer notes own implementation
constraints. README owns the project overview and generated book introduction;
edit README, not `docs/src/introduction.md`.

`docs/theme/` owns book presentation and follows the palettes in `mod/Themes/`.
The book cursor uses `docs/src/images/cursor-tame.png`, extracted from RimWorld's
`UI/Designators/Tame`, mirrored like the mod's `DeadCursor`, and scaled to 24px.
Interaction variants rotate that same hand on a 32px transparent canvas to avoid
clipping. Their CSS hotspots follow the original fingertip through each rotation;
retain transparency and update all variants together when replacing the asset.
`docs/theme/scrollbars.js` owns interactive scrollbar rails over native scrolling
areas. Native scrollbar controls can ignore custom cursors, so hide them only after
installing the replacement. Rail geometry and warm-theme colors follow `UiScrollbar`
and `UiTheme`; wheel, touch, and content keyboard scrolling stay browser-owned.
Rebase `index.hbs` when upgrading mdBook and update the docs workflow's version pin
together; the override otherwise hides upstream template fixes.

Put each fact on one main page; tours and FAQ link to detailed guides and references.
Give every FAQ question an explicit stable anchor.

The book groups user workflows in `SUMMARY.md`. Page directories mirror its top-level
sections; Introduction remains at the book root. Installation follows Getting started
and owns the Linux, macOS, and Sidecar mode setup guides. Keyboard and mouse shortcuts belong
in Reference, and the architecture overview belongs in Development. Terminal
interaction and shell guides belong under Terminals. Getting started owns Quickstart
and FAQ, in that order. Workspace begins with Interface and ends with `"Gameplay"`
(including the quotation marks), which owns colony behavior and Eco mode.
Workspace also owns command presets alongside project and file workflows. Agents owns
usage and summaries, keeping shared credentials, usage polling, and prompt summaries
together. Maintenance owns saves and profiles, storage, recovery, and
update/removal procedures. Installation links to the maintenance procedure.
Keep related short topics as page sections rather than creating a page per control.

Build and navigation maintenance belong to [Contributing](../docs/src/development/contributing.md#documentation).
`tools/docs/check_book.py` owns book validation and repository documentation link checks.
`tools/docs/constants.py` is the mdBook preprocessor for `{{#constant release_version}}`,
`{{#constant release_tag}}`, and `{{#constant release_tag_format}}`. It reads the
Cargo package version and shared tag conventions from `tools/version/metadata.py`;
unknown names fail the build. Use these in release examples instead of duplicating
version numbers. Build-time expansion leaves Markdown sources and the human-managed
changelog untouched. Documentation CI watches both sources of these values.
Public API contracts belong to the [API reference](../docs/src/reference/api.md);
wire formats and generator ownership belong to [wire protocol](protocol-wire.md).
