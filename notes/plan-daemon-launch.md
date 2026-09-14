# Launch plan and argv observability

`sandbox::build_argv` returns a flat vector and startup logs join it with spaces,
losing argument boundaries. Tmux errors can also include the complete argv. Add a
structured, redacted launch report that covers both success and failure paths.

## Goal

Keep sandboxed agent startup as direct argv through tmux while making the effective
layered launch readable, exact and inspectable. The stack is
`systemd-run` when limits are configured, `pasta` for private networking, then
`bwrap -> command`; host sessions use the separate host argv path.
Generated shell text is a report only; it is never the process entrypoint.

## Design

- Replace the opaque `sandbox::build_argv` result with a `LaunchPlan` that keeps
  ordered sections for limits, pasta, bwrap, environment, mounts and the final
  command. One lowering function produces the exact in-memory `Vec<String>` sent
  to tmux; host sessions remain outside this sandbox plan.
- Render the plan in two forms: a human view with sections and one argument per
  line, and JSON with boundary-preserving argv arrays. Shell quoting is
  presentation-only. Every external rendering is a sanitized projection, not
  the unsanitized in-memory argv.
- Apply one centralized redaction policy before logs, artifacts, API responses
  or observed `/proc` snapshots. Combine known-secret replacement with structural
  redaction of environment values (including `--setenv KEY VALUE` and `KEY=VALUE`)
  and sensitive command arguments in both separate-value and `--key=value` forms.
  Worker credentials must remain redacted even after a daemon restart has lost
  their original values. Do not persist a copy of worker credentials.
- Render only explicitly supported, safe argument fields; replace unknown command
  arguments with placeholders that preserve argument boundaries. Apply this
  fallback to descendant processes too: their credentials may never have appeared
  in the launch plan. Neither `ps` fallback output nor tmux error text may bypass
  the policy; omit raw command text when it cannot be safely parsed.
- Replace `start.rs`'s `argv.join(" ")` log with the human/structured renderer.
- Save the sanitized latest sandbox launch plan as
  `<state-root>/<state-id>/launch-plan.json`, atomically and mode `0600`.
  Durable state keeps it across daemon restarts and while configured-down; reset
  or delete moves it with the state to the existing 14-day trash, while
  ephemeral state cleanup removes it with the session. The protected state root
  keeps the artifact separate from guest-visible private files.

## Implementation order

1. Add the plan types and renderers. Refactor `sandbox/bind.rs` and
   `sandbox/bind/mounts.rs` to emit named sections without changing ordering.
2. Make `build_argv` lower the plan and preserve existing argv tests. Add tests
   for mount ordering, nested wrappers, shell-special arguments, empty values and
   redaction, including worker credentials in `--setenv` arguments. Write the
   process-observation renderer against the same redaction policy. Cover inspection
   after restart without the original secret values, descendant-only credentials,
   unknown positional arguments, and tmux/`ps` fallback output.
3. Write the plan during sandboxed start and use it for daemon logs. Host
   sessions have no sandbox plan; inspection should say so rather than inventing
   bwrap layers. Keep the artifact separate from guest-visible private files.
4. Add `slopctl sandbox inspect NAME` and an authenticated
   `GET /api/sessions/:name/sandbox` route. Use the existing session capability
   rules: root may inspect any session, while a scoped caller may inspect only a
   session it is granted. Always return sanitized data.
5. Add a tmux `#{pane_pid}` query and collect the live process tree by walking
   `/proc/<pid>/cmdline`, status/PPID links and, when needed, the systemd scope's
   cgroup. Use a `ps` snapshot only as a best-effort fallback while processes are
   live; if the pane has disappeared, report that there is no live tree and rely
   on the saved plan.
6. Update the sandbox diagnostics documentation and `notes/ops-paths.md` with
   the artifact location and retention behavior.

## Deliberate non-goals

- Do not execute generated scripts or introduce a shell wrapper; this would
   alter quoting, interpreter, PID, signal and exit-status behavior. Host `/tmp`
   starts as bwrap's `/tmp` tmpfs, subject to deliberate persistent, project or
   preset mounts that replace it.
- Do not use `bwrap --args FD` initially. The installed bwrap supports
  NUL-separated argument input, but passing a custom FD through detached tmux
  is awkward and pasta has no matching interface. Revisit it only if argv size
  becomes a measured problem.
- Do not treat the saved plan as proof of successful launch: `inspect` must be
  able to compare it with the live process tree.

## Verification

Run `make format-daemon`, `make lint-daemon`, and `make test-daemon`.
Verify that worker failures, intended plans, live process snapshots and all
rendered artifacts contain no bearer token or secret environment value, including
after restart and for credentials introduced only by descendants. Confirm
the saved file's `0600` mode, atomic replacement, durable/trash retention and
ephemeral cleanup.
