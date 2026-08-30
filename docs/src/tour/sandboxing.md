# Sandboxing

SlopWorld is not production-grade security software. It gives more isolation than running
agents unsandboxed on your desktop, but that is the extent of the guarantee.

**Back up your data** before using SlopWorld.

## Stack

Each agent runs inside a layered sandbox:

- **systemd-run** wraps the complete process tree in a transient user scope when resource
  limits are configured.
- **Bubblewrap** (bwrap) provides filesystem isolation through bind mounts.
- **pasta** provides network isolation with synthetic DNS.
- **tmux** owns the terminal session on a private socket (`slopworld`).

## Sandbox presets

A preset is one TOML file per tool, defining bind mounts, environment, and capabilities.
Builtins are compiled into the daemon; user files under `~/.config/slopworld/presets/`
replace builtins by name.

`global.toml` is implicit and precedes all other presets. It is not a project checkbox —
copying it to the user directory creates an override.

Path kinds in a preset:

- `ro` / `rw` — read-only or read-write bind mounts.
- `dev` — device bind mounts.
- `private` — per-session copy of a host path. State lives under
  `~/.local/share/slopworld/sessions/<state-id>/`. Missing files and named `seed`
  directories are copied once; `skip` excludes history and bulk state from seeding.
- An agent may opt into a persistent private `/tmp`; without that option `/tmp` is a fresh
  tmpfs each run. The persistent tree is part of the agent's state and is removed by reset or
  when a temporary errand finishes.
- `shared` — overlays a host-owned file read-write into private state, for rotating
  credentials.

Protected paths (`/`, `$HOME`, the daemon config, preset directory, and session state
root) are rejected from all bind lists. Invalid binds are warned and dropped.

Presets reload when the directory's mtime changes.

## Networking

Projects set a network default; agents may override it:

- **none** — bwrap's private network namespace. No connectivity.
- **private** — pasta wraps bwrap with synthetic DNS and no port forwarding. Blocks
  loopback but does not restrict egress.
- **host** — `--share-net`. Full host networking, including local services.

Private DNS follows up to two IPv4 nameservers from the daemon's `/etc/resolv.conf`.
A project or agent may configure explicit DNS servers instead; the choice is fixed at
agent startup.

## Process limits

Project and agent `limits` (`memory_mb`, `pids`, `nofile`, `cpu_pct`) are inherited, with
the agent value winning. A non-empty limit wraps the tree in a transient systemd scope.
Zero is rejected.

## Escape warnings

Non-empty `escapes` text on a preset marks a capability that reaches back toward the host.
The warning appears when an agent uses that preset.

## Remaining exposure

There is no seccomp, `--new-session`, or disk quota. Project directories (including `.git`)
are read-write, so agents can install hooks or alter git configuration. Resource caps are
off unless configured.

## Verifying the sandbox

When adding or editing an agent, visit the Preview tab to see the resolved sandbox
parameters. After spawning an agent, check the actual bwrap/pasta command line:

```sh
ps -ww -eo pid=,ppid=,user=,comm=,args= \
  | awk '$4 == "bwrap" || $4 == "pasta"'
```
