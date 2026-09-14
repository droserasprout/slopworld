# Human docs gaps

Scope: README, `docs/src/`, sidecar README and generated references. Verify against source;
[documentation ownership](docs-human-docs.md) applies.

- First-run path: prerequisites, agent CLI auth, service/endpoint health, first project/agent
  and successful task. Distinguish GOG-tested from expected Steam behavior.
- Support/distribution: architectures, DLC requirements, source versus archives, sidecar/macOS
  limits, and useful release/upgrade entries in CHANGELOG.
- Minimal valid config: project/agent/preset, inheritance/override, mounts, credentials,
  experimental discovery and restart boundaries.
- Sidecar make-target parity and reader preview/pin/close behavior in the tour.
- API examples: authenticated HTTP, WebSocket, errors and token handling. Keep routes generated.
- Maintenance: make root reference discoverable or explicitly developer-only; gate docs, prose
  and generated API drift in CI.

Use `make docs`, `make lint-prose`, `make api-docs` and `make check-reqs`; check local links.
Do not claim release installation or platform validation before those deliverables exist.
