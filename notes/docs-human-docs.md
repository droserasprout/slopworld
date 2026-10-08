# Human documentation

The mdBook in `docs/` owns public user procedures; developer notes own implementation
constraints. README owns the project overview and generated book introduction;
edit README, not `docs/src/introduction.md`.

`docs/theme/` owns book presentation and follows the palettes in `mod/Themes/`.
Rebase `index.hbs` when upgrading mdBook and update the docs workflow's version pin
together; the override otherwise hides upstream template fixes.

Put each fact on one main page; tours and FAQ link to detailed guides and references.
Give every FAQ question an explicit stable anchor.

The book groups user workflows in `SUMMARY.md`. Page directories mirror its top-level
sections; Introduction remains at the book root. Deployment follows Getting started
and owns the macOS and Linux sidecar setup guides. Keyboard and mouse shortcuts belong
in Reference, and the architecture overview belongs in Development. Terminal
interaction and shell guides belong under Terminals; colony behavior and Eco mode
belong under Getting started in
`"Gameplay"` (including the quotation marks).
Workspace owns command presets alongside project and file workflows. Agents owns
usage and summaries, keeping shared credentials, usage polling, and prompt summaries
together. Maintenance owns saves and profiles, storage, recovery, and
update/removal procedures. Installation links to the maintenance procedure.
Keep related short topics as page sections rather than creating a page per control.

Build and navigation maintenance belong to [Contributing](../docs/src/development/contributing.md#documentation).
`tools/docs/check_book.py` owns book validation and repository documentation link checks.
Public API contracts belong to the [API reference](../docs/src/reference/api.md);
wire formats and generator ownership belong to [wire protocol](protocol-wire.md).
