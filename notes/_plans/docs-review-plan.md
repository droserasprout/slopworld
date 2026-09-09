# Human docs review plan

Scope is `README.md`, `docs/src/`, `slopcar/README.md`, generated references, and the
documentation guidance in [human-docs](../human-docs.md).

## Goals

- Let a new user install, verify, and start a first agent without reading source or devnotes.
- Make Linux, macOS, sidecar, source, and release workflows distinguishable at a glance.
- Keep configuration, security, API, and UI behavior examples aligned with the implementation.
- Make generated documentation and prose quality part of the normal validation path.

## Open work

1. **Onboarding and requirements.** Expand `docs/src/requirements.md` beyond the short
   dependency list, or clearly delegate to `make check-reqs`. Cover runtime tools such as
   `rg`, `less`, `micro`, `highlight`, `gio`, `gdbus`, `libasound`, and the supported agent
   CLIs. Add a first-run checklist to `docs/src/install.md`: service status, `slopctl status`,
   endpoint/profile locations, and creating the first project and agent.
2. **Support and distribution matrix.** Fill the remaining matrix gaps: sidecar architectures,
   DLC requirements, release archives versus source installs, and a useful release-entry format
   in `CHANGELOG.md`. Keep the existing GOG-tested versus expected-Steam distinction explicit.
3. **Configuration examples.** Add a minimal valid `config.toml` covering one project, one
   agent, a command preset, sandbox/network inheritance, and an override. Explain the UI/raw
   config boundary, precedence, temporary projects, mounts, credentials, breadcrumbs, and
   which changes require restart.
4. **Sidecar command parity.** Add the `make sidecar-build`, `make sidecar-doctor`, and
   development-loop equivalents beside the direct `slopcar` commands, and verify their names
   against the Makefile.
5. **User-visible UI behavior.** Update `docs/src/tour/interface.md` with the replaceable
   preview, double-click-to-pin, routed-header, and close/lifetime behavior.
6. **Practical API reference.** Extend `docs/src/reference/api.md` with authenticated `curl`
   requests, a WebSocket example, representative request/response bodies, error handling, and
   token guidance. Keep `api-routes.md` generated.
7. **Documentation maintenance.** Make the generated root `reference.md` discoverable or
   explicitly developer-only. Add CI coverage for `make docs`, `make lint-prose`, and a clean
   generated API diff so documentation drift fails close to its cause.

## Validation

- Check every new command and default against the Makefile, launcher help, config model, and
  sidecar wrapper.
- Run `make docs`, `make lint-prose`, and `make check-reqs` on the final change.
- Regenerate API documentation with `make api-docs` and confirm no unexpected diff remains.
- Recheck relative links and verify that the curated `AGENTS.md` links stay current.

## Decisions to settle during implementation

- Whether distro-specific package commands belong in the main requirements page or a separate
  Linux guide.
- Whether release installation should be documented before a published release archive exists.
