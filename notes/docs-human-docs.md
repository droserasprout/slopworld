# Human documentation

The mdBook in `docs/` owns public user procedures; developer notes own implementation
constraints. Verify behavior claims against source and tests when resolving a
conflict. The README keeps a short Linux quickstart and links to platform guides.

Put each fact on one main page. Tours introduce workflows and the FAQ answers
recurring questions; both link to detailed guides and references. Do not duplicate
option tables, configuration steps, or troubleshooting procedures in tours or the
FAQ. Give every FAQ question an explicit stable anchor.

Build and navigation maintenance belong to [Contributing](../docs/src/reference/contributing.md#documentation).
Public API contracts belong to the [API reference](../docs/src/reference/api.md);
wire formats and generator ownership belong to [wire protocol](protocol-wire.md).
