# What keeps an agent off the host

Three rules in `sandbox.rs`, all of them structural: none is a thing to
remember at the moment a checkbox is ticked. The invariant they serve is the
one in [agent-grants](agent-grants.md) - an agent reaches the sessions it was
granted and never the machine.

## No bind list reaches the token

`refused()` is checked by `paths()` (so the implicit `global` preset, every preset
file and every project pass through it), by `check_project` where a directory
is typed, and by `start` for an entry older than the check. Presets additionally
pass through the centralized validator before the daemon saves or uses them:
private, seed, skip and shared paths must stay within the declared private tree,
and the complete dependency closure must be valid. Refused in **both directions**:
a path inside one of these reaches it, a path above one contains it, and
`~/.config` is as much a road to the token as `config.toml` is.

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
This is not about privacy: `~/.claude/settings.json` names hooks and
`~/.claude.json` names MCP servers, and both are command lines the *host's*
Claude Code runs the next time it starts. A sandbox that can write them has a
delayed shell on the far side of the wall. It falls out that two agents no
longer read each other's transcripts either.

- Lives in `~/.local/share/slopworld/sessions/<session>/`, keeping the shape of
  the original (`home/.claude`, `root/etc/x`) so two preset paths sharing a
  basename never land on one directory. `SLOPD_STATE` moves it; the tests use
  that.
- Seeded **once**, when the copy is absent: the files at the top of the host
  directory whatever they are called - that is where a tool keeps its
  credentials - plus the subdirectories `seed` names, which is where what the
  *user* wrote lives. Deliberately not a list of every agent's filenames: the
  failure it chooses is a session that copied a megabyte it did not need, over
  one that cannot log in.
- `skip` cuts back out of what `seed` names **and** out of the files on top,
  because "whatever they are called" catches what a tool wrote *about* the user
  next to what it wrote *for* them. `~/.claude/history.jsonl` is 1.1MB of every
  prompt typed on this machine, in every project, and was going into every
  sandbox until it was named. That is what makes naming a whole
  directory the right move. A tool scatters its config and concentrates its
  bulk: `~/.pi/agent` holds the model selection *and* 21MB of transcripts, so
  the preset seeds `agent` and skips `agent/sessions`. Listing by hand the files
  that turn out to matter is how `pi` shipped seeding `agents`, `extensions` and
  `prompts` - three directories pi has never made - and agents came up having
  forgotten which model they were. Seed wide, skip the bulk, fail towards an
  agent that works.
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

A credential is not state, it rotates. Claude Code's access token lasts 8 hours
and the refresh token behind it is replaced on every use, sliding an 11-day
window forward; a tree seeded once holds whichever token was current the day the
session was first started. So every session logged in until it didn't, and the
only cure was deleting the session directory - which is also how its transcripts
went. Those were one bug, not two.

- **Files only.** `shared_binds` drops a directory, and that narrowness is the
  whole of what makes this safe. `.credentials.json` names no command; the
  `settings.json` beside it names hooks and `~/.claude.json` names MCP servers,
  and a shared *directory* is a sandbox that can create either. What is traded
  here is integrity - something inside can log the user out - and never
  execution. That is why the preset is not marked `escapes`.
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
