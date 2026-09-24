# Human docs gaps

Status: proposed

Scope: README, `docs/src/`, sidecar README, and generated references.
Check descriptions against source. [Documentation ownership](docs-human-docs.md) applies.

- Connect the existing requirements and installation guides into an end-to-end first-run
  path: agent CLI auth, service/endpoint health, first project/agent and successful task.
- Support/distribution: architectures, DLC requirements, source versus archives, sidecar/macOS
  limits, and useful release/upgrade entries in CHANGELOG.
- Add a minimal valid project/agent/preset configuration example, linking existing ownership,
  mount and restart documentation. Cover credential setup and experimental-feature discovery.
- Sidecar make-target parity and reader preview/pin/close behavior in the tour.
- API examples: authenticated HTTP, WebSocket, errors and token handling. Keep routes generated.
- Maintenance: make the root reference easy to find or explicitly identify it as developer documentation.
  Require CI checks for docs and generated API changes.

Use `make docs`, `make api-docs`, and `make check-reqs`.
Check local links.
Do not claim release installation or platform validation before those deliverables exist.
