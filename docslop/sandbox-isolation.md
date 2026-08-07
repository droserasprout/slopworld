# What keeps an agent off the host

Three rules in `sandbox.rs`, all of them structural: none is a thing to
remember at the moment a checkbox is ticked. The invariant they serve is the
one in [agent-grants](agent-grants.md) - an agent reaches the sessions it was
granted and never the machine.

## No bind list reaches the token

`refused()` is checked by `paths()` (so the global `[sandbox]`, every preset
file and every project pass through it), by `check_project` where a directory
is typed, and by `start` for an entry older than the check. Refused in **both
directions**: a path inside one of these reaches it, a path above one contains
it, and `~/.config` is as much a road to the token as `config.toml` is.

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
- `skip` cuts back out of what `seed` names, which is what makes naming a whole
  directory the right move. A tool scatters its config and concentrates its
  bulk: `~/.pi/agent` holds the model selection *and* 21MB of transcripts, so
  the preset seeds `agent` and skips `agent/sessions`. Listing by hand the files
  that turn out to matter is how `pi` shipped seeding `agents`, `extensions` and
  `prompts` - three directories pi has never made - and agents came up having
  forgotten which model they were. Seed wide, skip the bulk, fail towards an
  agent that works.
- `seed` comes from two places: the **preset**, for what every session of that
  software wants, and the **project**, for what one ground wants. `~/.claude/plugins` is the case the project layer was added
  for - 13MB of language servers and marketplace clones, worth copying into the
  session that will open one and not into the thirty that will not. A project
  may name the whole directory or a single plugin inside it; a seed path that
  falls under no private path is skipped.
- A `private` entry that is a file is its own seed. One whose host path does
  not exist is skipped - nothing to keep separate from a file nobody has.
- Bound *after* the ro/rw/dev binds, so a project naming `~/.claude` in its own
  `rw_paths` gets the copy anyway. There is no way to ask for the original.
- `prepare_private` runs in `start`, before the argv: bwrap binding a source
  that is not there is a session that will not start. `build_argv` stays pure,
  because the tests call it and own no home.
- **Deleting a session's directory is how it is handed a fresh one.** Nothing
  re-seeds on its own, so what the host changed afterwards stays out.

## A way out says so

`escapes` is free text on a sandbox preset, non-empty when ticking it hands the
sandbox a road back: `docker`, `podman`, `dbus`, `systemd`, `x11`, `ssh`,
`1password`. The mod draws those in `Warn` and puts the sentence first in the
tooltip - before the description, because by the time the eye reaches a list of
paths the decision has been made. A preset that merely carries a *secret*
(`aws`, `kube`, `gh`) is not marked: that is a trade about reach, and this one
is about the wall.

`presets::tests` holds both facts, since both are properties of the shipped
files rather than of any code.

## Not done

- **The loopback is shared.** `net = true` is `--share-net`, so the daemon on
  `127.0.0.1:7717` is inside every sandbox - only `[daemon] token` stands in
  front of it, and that token is empty by default. Wants a private netns
  (pasta) and the API on a unix socket bound into granted sandboxes only.
- No seccomp, and no `--new-session` (which would drop the pane's controlling
  tty and take job control with it - a real trade, not a free win).
- No `TasksMax`, `MemoryMax` or disk quota: a fork bomb in a sandbox is a host
  outage. The tmux servers already go through `systemd-run`, so a transient
  scope per session is the short path.
- The project directory is bound read-write, `.git` included, so an agent can
  set hooks or `core.fsmonitor` that the host runs on its next git command
  there. Left deliberately: the project directory is the agent's.
