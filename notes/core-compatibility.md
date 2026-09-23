# Compatibility

SlopWorld is pre-0.0.1. Do not preserve legacy wire fields, path aliases, config names or
fallback routes for compatibility. Change the daemon, mod, and notes together.
Changes may invalidate stale clients and old local state instead of migrating them. Remove old paths
or fail explicitly. Do not silently accept both forms.
