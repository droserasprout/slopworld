# Compatibility

SlopWorld is pre-0.0.1. Do not preserve legacy wire fields, path aliases, config names or
fallback routes for compatibility. Change the daemon, mod and notes together; stale clients
and old local state may be invalidated rather than migrated. When an old path is found, remove
it or make the break explicit instead of silently accepting both forms.

The former path-only station setting, WebSocket `source` field, daemon usage switches, and
legacy shortcut/jukebox shapes have been removed. Configured sessions without a state id are
rejected; old state is not assigned an identity on first use. The `install_ost.py` references
to old OST filenames only delete them, so that remains cleanup rather than a compatibility path.

The daemon's own startup loader converts the three former usage switches to per-row settings
once and removes them from `config.toml`. The general parser remains strict, so an old client
cannot write the switches back through the configuration API.
