# Data externalization candidates

Consider theme palettes, loading tips, simulation tuning and appearance catalogs in that
order; launcher profiles and icon-manifest generation are lower priority. This is proposed
work, not a requirement to externalize every constant.

Keep machine config, profile preferences and packaged content separate. The mod's flat
settings parser cannot read structured catalogs; use a real parser or build-time generator.
Builtin rendering/simulation must work with the daemon offline.

Each migration needs an explicit runtime/compiled/generated ownership model, validated typed
records, deterministic ordering, and safe fallback. Keep user overrides separate from shipped
data. Simulation tuning additionally needs missing-def and save-compatibility decisions;
themes need complete role validation. Test malformed/partial inputs without Unity where possible.

Leave wire vocabulary, security policy, geometry/algorithms, process lifetime and provider
adapters in code. Do not externalize command callbacks or labels without a concrete need.
Run affected make tests/lint and update the owning note when a catalog actually moves.
