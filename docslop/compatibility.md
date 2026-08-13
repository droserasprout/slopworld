# Compatibility

SlopWorld is pre-0.0.1. Do not preserve legacy wire fields, path aliases, config names or
fallback routes for compatibility. Change the daemon, mod and notes together; stale clients
and old local state may be invalidated rather than migrated. When an old path is found, remove
it or make the break explicit instead of silently accepting both forms.

The audit found two remaining compatibility readers: `Radio.FindSelection` accepts path-only
station settings, and the audio WebSocket accepts the old `source` field. `install_ost.py` only
mentions old OST filenames to remove them, so that is cleanup rather than a compatibility path.
