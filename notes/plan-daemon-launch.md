# Launch plan and argv observability

`sandbox::build_argv` returns a flat vector and startup logs join it with spaces,
losing argument boundaries. Add a structured, redacted launch report.

## Goal

Keep agent startup as direct argv through tmux while making the layered launch
(`systemd-run -> pasta -> bwrap -> command`) readable, exact and inspectable.
Generated shell text is a report only; it is never the process entrypoint.

## Design

- Replace the opaque `sandbox::build_argv` result with a `LaunchPlan` that keeps
  ordered sections for limits, pasta, bwrap, environment, mounts and the final
  command. One lowering function produces the exact `Vec<String>` sent to tmux.
- Render the plan in two forms: a human view with sections and one argument per
  line, and JSON with exact argv arrays. Shell quoting is presentation-only.
- Redact environment values and other secrets before logs, artifacts or API
  responses. Do not persist a copy of worker credentials.
- Replace `start.rs`'s `argv.join(" ")` log with the human/structured renderer.
- Save the sanitized latest launch plan below the daemon-owned session state,
  atomically and mode `0600`; durable state keeps it for postmortem inspection,
  while ephemeral state cleanup removes it with the session.

## Implementation order

1. Add the plan types and renderers. Refactor `sandbox/bind.rs` and
   `sandbox/bind/mounts.rs` to emit named sections without changing ordering.
2. Make `build_argv` lower the plan and preserve existing argv tests. Add tests
   for mount ordering, nested wrappers, shell-special arguments, empty values and
   redaction.
3. Write the plan during start and use it for daemon logs. Keep the artifact
   separate from guest-visible private files.
4. Add `slopctl sandbox inspect NAME` (and its authenticated API route) showing
   the intended plan plus the actual process tree from the tmux pane PID and
   `/proc/*/cmdline`; retain the existing `ps` fallback for a dead process.
5. Update the sandbox diagnostics documentation and `notes/ops-paths.md` with
   the artifact location and retention behavior.

## Deliberate non-goals

- Do not execute generated scripts or introduce a shell wrapper; this would
  alter quoting, interpreter, PID, signal and exit-status behavior. Host `/tmp`
  is also hidden by bwrap's `/tmp` tmpfs.
- Do not use `bwrap --args FD` initially. The installed bwrap supports
  NUL-separated argument input, but passing a custom FD through detached tmux
  is awkward and pasta has no matching interface. Revisit it only if argv size
  becomes a measured problem.
- Do not treat the saved plan as proof of successful launch: `inspect` must be
  able to compare it with the live process tree.

## Verification

Run `make format-daemon`, `make lint-daemon`, and `make test-daemon`.
Verify that worker failures and all rendered artifacts
contain no bearer token or secret environment value.
