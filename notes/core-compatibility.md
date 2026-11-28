# Compatibility policy

SlopWorld contracts support only their current format. Do not add migrations,
historical-format fallbacks, or checks for retired storage locations. Update every
affected participant and focused note when changing a contract. Each store owns
schema validation and interrupted-transaction recovery.

Concrete wire decisions belong to [wire protocol](protocol-wire.md), persistence
decisions to [configuration stores](daemon-config-stores.md) and their focused
catalog owners, and public route guarantees to the [API reference](../docs/src/reference/api.md).
