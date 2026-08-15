# Refactor: SlopOptions god class

`mod/Source/SlopWorld/Patches/SlopOptions.cs` — 112 members (29 methods, 38
fields). The biggest class in the mod and the one that grows with every new
options page. Highest-value refactor.

The smell is three **parallel arrays** keyed by hand, one triple per page:

1. a `_config … _about` field (18 of them),
2. a `XxxCategory => _xxx?.Def` accessor (18 of them, pure boilerplate),
3. a cached `_xxxPage` instance (13 of them).

A `Tab` already carries `Def`, `Icon`, `Page`, `Parent` — so the standalone
fields and their 18 accessors duplicate what `Column` already holds.

Actionable:

- [ ] Give `Tab` a stable `Key` (enum or string). Look tabs up via
      `Column.First(t => t.Key == …)` or a `Dictionary<PageId, Tab>` instead of
      the 18 named fields.
- [ ] Collapse the 18 `XxxCategory` properties into one `CategoryFor(key)` (or
      drop them where callers can hold the `Tab`).
- [ ] Let each `Tab` own or lazily build its page, retiring the 13 `_xxxPage`
      fields. `Install` becomes a table of `(key, icon, page-factory, parent)`
      rows rather than hand-wired statics.

Net: ~50 members collapse into data. Watch the rebuilt-per-open contract
(comment at the `_page` field) — pages must still be re-created on open so a
config edited elsewhere is re-read.
