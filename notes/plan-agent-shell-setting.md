# Investigate Agent shell setting not selecting the agent tool shell

With SlopWorld's **Agent shell** set to Bash, a Codex session advertised zsh in
its environment context and executed commands through zsh when the tool call
omitted its shell argument. Explicit `/bin/bash` worked. The setting must be
investigated as a user-visible behavior gap even if `SHELL` is passed correctly.
Owners: [configuration](daemon-config-stores.md), [sandbox](sandbox-isolation.md),
and the [documented shell boundary](../docs/src/guides/configuring-agents.md#shell).

## Evidence and uncertainty

- Investigation task `001a0b081e653-0007` reported `SHELL=bash` in the Codex exec
  process while its executable was `/bin/zsh`; the account login shell and systemd
  user-manager environment pointed to `/bin/zsh`. No settings or source were changed.
- `sandbox/bind/mounts.rs` applies `defaults.agent_shell` as `SHELL` after preset
  environment values. The documented setting currently promises that environment
  variable, but also describes avoiding the host login shell in agent tools.
- `sandbox/host.rs` deliberately uses the daemon's inherited shell for host panes.
  Host panes and shell errands have separate ownership and are not evidence that
  sandboxed agent settings are applied incorrectly.
- Account-shell lookup is a candidate explanation for Codex's choice. Confirm the
  installed client's actual selection precedence, including cached session metadata;
  the worker's conclusion alone does not establish that implementation path.

## Investigation and acceptance

1. Trace the saved setting through the config API, launch resolution, sandbox argv
   and a freshly started agent. Compare existing/resumed sessions with fresh ones;
   the documented setting requires an agent restart.
2. Reproduce with host login shell zsh and Agent shell Bash. Record only shell-related
   settings, `SHELL`, account lookup and actual child executable. Inspect the installed
   Codex implementation/configuration to establish how it chooses the default shell
   when no per-command override is supplied. Do not expose credentials.
3. Identify a supported way for SlopWorld to make the selected shell effective for
   agent tools. Evaluate client launch configuration or sandbox identity metadata
   only after confirming precedence. A per-call Bash override is a workaround;
   changing the user's global login shell or host service environment is not a fix
   for this setting's sandboxed-agent behavior.
4. Add a focused regression check at the responsible launch boundary and verify a
   fresh agent's default command shell follows the setting, including another valid
   shell value. Keep explicit tool overrides, host panes and shell errands independent.
   Run the relevant Makefile tests. If a client cannot honor the setting, document
   the precise limitation and make the UI promise accurate rather than claiming success
   from `SHELL` alone.

Do not change global account/service settings or launch the game during investigation.
Update the focused ownership note and user guide with the resolved contract, then
delete this plan.
