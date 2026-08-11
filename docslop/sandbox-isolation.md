# What keeps an agent off the host

Three structural rules in `sandbox.rs` enforce the [agent-grants](agent-grants.md)
invariant: an agent reaches granted sessions, never the machine.

## No bind list reaches the token

`refused()` covers implicit and explicit presets, projects, and legacy entries at
start. Preset validation also requires private, seed, skip, and shared paths to stay
within their private tree, across the full dependency closure. Refusal works in both
directions: a protected path and any ancestor that contains it are forbidden.

- `/` and `$HOME` itself.
- `Config::path_in_use()` - `SLOPD_CONFIG` included, which is why that lookup
  moved out of `main` and onto `Config`.
- `Table::dir()`, the preset files. Writing one decides what the *next*
  sandbox binds.
- `state_root()`, below.

A bind list warns and drops the path, the way an unknown preset name is
dropped: the files outlive the binary. A project *directory* is refused
outright instead - it is bound read-write and no preset can drop it.

## Every agent's state is its own

`private = [...]` on a sandbox preset binds a per-session copy over the host
path, so a program that looks under `$HOME` finds one and never the user's.
This prevents delayed host execution through writable hook or MCP configuration;
separating agents' transcripts is a secondary benefit.

- Lives in `~/.local/share/slopworld/sessions/<session>/`, keeping the shape of
  the original (`home/.claude`, `root/etc/x`) so two preset paths sharing a
  basename never land on one directory. `SLOPD_STATE` moves it; the tests use
  that.
- Seeded **once**, when absent: top-level files plus named `seed` subdirectories.
  This favors copying some harmless excess over missing required configuration.
- `skip` excludes both seeded descendants and top-level files. Seed broad config
  areas and skip history or bulk state (for example `agent/sessions`); enumerating
  guessed filenames has already omitted required model selection.
- `seed` comes from the **preset**, for what every session of that software wants.
  If one project or agent needs extra seeded state, make a user preset and attach it
  there. A seed path that falls under no private path is skipped.
- A `private` entry that is a file is its own seed. One whose host path does
  not exist is skipped - nothing to keep separate from a file nobody has.
- Bound *after* the ro/rw/dev binds, so an ordinary writable preset bind gets the copy anyway.
  There is no way to ask for the original.
- `prepare_private` runs in `start`, before the argv: bwrap binding a source
  that is not there is a session that will not start. `build_argv` stays pure,
  because the tests call it and own no home.
- **Deleting a session's directory is how it is handed a fresh one.** Nothing
  re-seeds on its own, so what the host changed afterwards stays out.

## Except the credential, which cannot be a copy

`shared = [...]` binds the **host's own file** read-write, after the private
binds and so on top of them: a hole cut in a copy, one file wide. There is one,
`~/.claude/.credentials.json`.

A credential rotates, so a seeded copy eventually expires. Sharing the host file keeps
refreshes current without deleting the session and its transcripts.

- **Files only.** A shared directory could create hook or MCP configuration. Sharing
  the credential risks logout (integrity), not host execution, so it is not `escapes`.
- `refused()` applies, the same as every other bind list.
- Never seeded. It is bound from the host anyway, and a copy would leave a
  superseded token in the session directory for as long as the session lives.
  bwrap makes the mount point itself - an empty file the bind covers.
- **A rename onto a bind mount fails with `EBUSY`**, so this only works because
  Claude Code writes credentials tmp-then-rename with an in-place
  `O_WRONLY|O_CREAT|O_TRUNC` fallback on `EXDEV`/`EPERM`/`EEXIST`/`EBUSY`. That
  fallback is load-bearing and belongs to somebody else's binary: a tool that
  renames without one shares nothing, silently, and the check before adding a
  second `shared` path is whether its writer has the same fallback.

## A way out says so

`escapes` is free text on a sandbox preset, non-empty when ticking it hands the
sandbox a road back: `docker`, `podman`, `dbus`, `systemd`, `x11`, `ssh-agent`,
`gpg-agent`, `1password`. The mod draws those in `Warn` and puts the sentence first in the
tooltip - before the description, because by the time the eye reaches a list of
paths the decision has been made. A preset that merely carries a *secret*
(`aws`, `kube`, `gh`) is not marked: that is a trade about reach, and this one
is about the wall.

`presets::tests` holds both facts, since both are properties of the shipped
files rather than of any code.

## Network modes

The project owns a network **ceiling** and an agent may only reduce it. The
three values are `none`, `private` and `host`; an omitted agent value inherits
the project. A project set to `private` therefore offers only `none` and
`private` to its agents, while a `host` project offers all three.

- `none` keeps bubblewrap's private network namespace and does not share it.
- `host` uses bubblewrap's `--share-net`, preserving access to local services.
  A `POST /api/run` errand with `host` remains an unsandboxed host session and
  is a separate, explicit path.
- `private` wraps the bubblewrap command in `pasta`. It gives the sandbox a
  synthetic IPv4 address and gateway, forwards DNS through host resolvers, and
  explicitly disables TCP/UDP port forwarding in both directions. The private
  resolver file is session-owned and is prepared before the bind argv is built;
  on hosts where `/etc/resolv.conf` points into `/run`, the daemon binds the
  replacement to the canonical target because `/run` is not otherwise in the
  sandbox.

The mod exposes the ceiling on the project editor, and an `Inherit / None /
Private / Host` override on the agent editor. The preview shows the effective
answer. The daemon validates the ceiling again on every add, update and start,
so a hand-edited config cannot widen an agent through the UI.

The private namespace removes the daemon's loopback from the agent. The grant
model still has a planned unix-socket handoff for agents that need scoped API
access; it is not implied by enabling networking.

## Not done

- No seccomp, and no `--new-session` (which would drop the pane's controlling
  tty and take job control with it - a real trade, not a free win).
- No `TasksMax`, `MemoryMax` or disk quota: a fork bomb in a sandbox is a host
  outage. The tmux servers already go through `systemd-run`, so a transient
  scope per session is the short path.
- The project directory is bound read-write, `.git` included, so an agent can
  set hooks or `core.fsmonitor` that the host runs on its next git command
  there. Left deliberately: the project directory is the agent's.
