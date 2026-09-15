# Data externalization candidates

## First migration: theme catalogs (implemented)

Move the `UIScheme` and `TerminalTheme` palette tables into TOML to make packaged themes
easier to edit and review. Validate them at build time and load them at runtime into typed
C# records, keeping user overrides separate from shipped
data. Builtin rendering must work with the daemon offline.

Preserve stable IDs, colors, alpha handling, derived roles and Match UI behavior. Pin house
themes first and sort the remaining themes alphabetically by ID.
Validate unique IDs, complete theme roles, valid colors and exactly 16 ANSI slots. Invalid
shipped data must fail the build. Test malformed/partial catalogs and equivalence with the
existing palettes without Unity where possible. Follow [UI identity](mod-ui-identity.md).

## Boundaries and later candidates

Keep machine config, profile preferences and packaged content separate. The mod's flat
settings adapter deliberately rejects structured data; Tomlyn already handles TOML grammar.
Any runtime catalog needs its own schema and reader, not necessarily another parser.

After themes, reassess loading tips and appearance catalogs against a concrete editing need.
Loading tips must preserve Grandma-mode filtering; represent eligibility explicitly instead
of punctuation suffixes. Defer simulation tuning until there is a concrete use case and
missing-def and save-compatibility decisions. Launcher profiles and icon-manifest generation
remain lower priority. This is not a requirement to externalize every constant.

Each later migration needs an explicit runtime/compiled/generated ownership model, validated
typed records and deterministic ordering. Keep any user overrides separate from shipped data;
invalid overrides need useful diagnostics and safe fallback, while invalid shipped data must
fail validation. Builtin rendering/simulation must remain usable with the daemon offline.

Leave wire vocabulary, security policy, geometry/algorithms, process lifetime and provider
adapters in code. Do not externalize command callbacks or labels without a concrete need.
Run affected make tests/lint and update the owning note when a catalog actually moves.
Wire generation into Makefile dependencies; if adding a shipped top-level directory, update
the installer too. See [build entry points](build-commands.md). Delete this plan when its
remaining candidates have been resolved or dismissed.
