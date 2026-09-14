# What an agent can destroy of its own $HOME

A sandbox preset gives an agent a `$HOME` mixing per-session private copies with
host-owned `shared` files. A recursive delete inside that tree succeeds on the copies
and stops at the shared files. The mounts themselves are in
[sandbox-isolation](sandbox-isolation.md).

## Blast radius of `rm -rf ~/.claude`

The private tree at `~/.local/share/slopworld/sessions/<state-id>/home/.claude` is
removed. It holds that session's agent state, shell snapshots and history, and the
daemon reseeds it on the next start.

The delete then fails on `~/.claude/.credentials.json`, which is a `shared` overlay of
the host file.

`rm -rf` walks depth-first, so the private tree is already gone when that refusal
happens. The command exits non-zero after removing everything below the shared file.

## Why the credential survives

No daemon check refuses the delete. The file survives because it is a mountpoint.

A `shared` entry is bound read-write, so an agent can truncate or overwrite its
contents in place. Deletion is the only operation the mount blocks.

The mount is not a containment boundary. A `shared` path is a host file an agent may
rewrite at any time.

## Telling from inside

`/proc/self/mountinfo` is the reliable way to tell a private copy from a shared host
file before deleting either.
