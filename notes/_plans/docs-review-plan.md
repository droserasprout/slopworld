# Human docs review plan

Baseline: 2026-09-08. Scope is `README.md`, `docs/src/`, `slopcar/README.md`,
generated references, and the documentation guidance in [human-docs](../human-docs.md).

The initial audit found no broken relative Markdown targets, and the checked-in API route
inventory matches `tools/api_docs.py`. `make check-reqs` passed all required checks in the
current development environment. The docs build and prose-lint commands are available through
the Makefile but were not run during the read-only audit.

## Goals

- Let a new user install, verify, and start a first agent without reading source or devnotes.
- Make Linux, macOS, sidecar, source, and release workflows distinguishable at a glance.
- Keep configuration, security, API, and UI behavior examples aligned with the implementation.
- Make generated documentation and prose quality part of the normal validation path.

## Work order

1. **Onboarding and requirements.** Expand `docs/src/requirements.md` beyond the short
   dependency list, or clearly delegate to `make check-reqs`. Cover runtime tools such as
   `rg`, `less`, `micro`, `highlight`, `gio`, `gdbus`, `libasound`, and the supported agent
   CLIs. Add a first-run checklist to `docs/src/install.md`: service status, `slopctl status`,
   endpoint/profile locations, and creating the first project and agent.
2. **Support and distribution matrix.** Document native Linux, native macOS plus Docker,
   and standalone sidecar workflows; tested GOG versus expected Steam support; sidecar
   architectures; DLC requirements; and release archives versus source installs. Align
   `docs/src/updating.md` with the release workflow and give `CHANGELOG.md` a useful release
   entry format.
3. **Configuration examples.** Add a minimal valid `config.toml` covering one project, one
   agent, a command preset, sandbox/network inheritance, and an override. Explain the UI/raw
   config boundary, precedence, temporary projects, mounts, credentials, breadcrumbs, and
   which changes require restart.
4. **Sidecar canonicalization.** Reconcile `docs/src/guides/sidecar.md` with `slopcar/README.md`.
   Choose the canonical operational page and link to it from the other; cover workspace path
   identity, credentials, ports, reuse versus rebuild, lifecycle commands, security limits, and
   the `make sidecar-*` development equivalents.
5. **User-visible UI behavior.** After the current preview-tab work lands, update
   `docs/src/tour/interface.md`, `notes/mod-ui-files.md`, and `notes/mod-ui-git.md` with the
   replaceable-preview, double-click-to-pin, routed-header, and close/lifetime behavior.
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
- Recheck relative links and verify that `notes/index.md` and the curated `AGENTS.md` links stay
  current.

## Decisions to settle during implementation

- Whether distro-specific package commands belong in the main requirements page or a separate
  Linux guide.
- Whether `slopcar/README.md` remains a technical companion or becomes the sidecar guide's
  source of truth.
- Whether release installation should be documented before a published release archive exists.
