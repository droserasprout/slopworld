# Launch observability

Sandbox startup currently flattens argv in logs, losing boundaries and risking secret
exposure through tmux errors. Add a structured launch plan in `sandbox/`, lowered once to
the exact argv passed to tmux. Host sessions remain a separate path; generated shell text
is display-only and must never become the entrypoint.

Acceptance constraints:

- Keep ordered limits/pasta/bwrap/environment/mount/command sections. Human and JSON views
  are sanitized projections; only in-memory execution keeps raw values.
- Centralize structural redaction for env and sensitive arguments, including worker tokens
  after restart when original secret values are gone. Unknown arguments need placeholders.
  Descendant-only credentials, tmux errors and `ps` fallbacks cannot bypass this policy.
- Atomically save mode-0600 `launch-plan.json` under the protected private-state identity.
  Durable plans survive restart/down state and follow reset/delete into trash; ephemeral
  cleanup removes them. Never mount the artifact into the guest.
- Add authenticated `GET /api/sessions/:name/sandbox` and `slopctl sandbox inspect NAME`.
  Root/scoped access follows existing session authority. Compare the saved intent with a
  live tree rooted at tmux's pane PID, using `/proc` and applicable cgroups; missing live
  processes leave the saved plan available, not evidence of successful launch.

Test lowering/order/quoting, redaction across every output path, file permissions and state
retention through `make test-daemon` and `make lint-daemon`. Preserve direct argv execution
and existing process/signal semantics. Defer FD-based bwrap argv until size is a measured problem.
