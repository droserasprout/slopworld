# Host-visible project caches

Status: implemented

## Problem

Cache mounts exist only in an agent's Bubblewrap namespace. An agent with access to the
main checkout can run a build in a nested `.worktrees/<name>` checkout, but that path
does not acquire the sibling checkout's cache mount. Host terminals and other host
processes also see an ordinary directory at a cache destination.

## Proposed behavior

- For each relative cache destination, create a symlink in the main checkout and each
  registered checkout to one stable, writable cache directory. The host follows the
  link without a host mount.
- Bind that cache directory at the same absolute path in each agent sandbox so the
  symlink resolves there too. Do not bind the cache over the checkout destination.
  Absolute cache destinations retain their existing semantics.
- Store managed caches at `<cache-root>/<project-id>/<cache-key>` (normally under
  `~/.cache/slopworld`; honor `SLOPD_CACHE`). Use the stable project ID rather than its
  editable display name. Derive the readable cache key from the full relative
  destination so nested paths and equal basenames cannot collide. Preserve existing
  managed cache data when changing the storage layout; external sources stay literal.
- Keep cache storage independent of worktree and worker lifetimes. Concurrent builds
  still share writable contents and require a cache safe for concurrent access.

## Lifecycle and safety

- Reconcile links when saving cache configuration and when creating or registering a
  worktree. Require the expected link before launch; report a changed or missing link
  instead of silently replacing user content. Refuse to replace an existing file,
  directory, or link with a different target. Provide an explicit migration path for
  preexisting build output at a destination.
- When removing a managed worktree, remove only daemon-owned links that still point
  to their recorded sources, before the cleanliness check. A changed link remains
  user content and blocks removal. On failed removal, preserve or restore the links
  and leave the worktree record inspectable. Renaming a checkout moves its links with
  it; renaming a project must not change cache identities.
- Validate resolved source and destination paths against protected roots and checkout
  overlap at every boundary that creates links or mounts. Ensure private sandbox
  overlays cannot hide the cache source. Do not follow a changed link while deleting
  it. Existing rules against replacing the checkout root or Git metadata still apply.
- In sidecar mode, a host-visible link works only if its target is reachable at the
  same path on both the host and in the container. Define that path mapping before
  enabling host-visible links there; otherwise fail configuration clearly.

## Completion checks

- A build from main that enters a sibling checkout sees the same cache as a session
  attached directly to that checkout, and a host process sees the same files.
- Creating, registering, renaming, and removing worktrees handle links without
  deleting cache data or user files. Removal still rejects unrelated ignored output.
- Project rename, cache configuration changes, daemon restart, and interrupted
  operations preserve data and expose actionable errors for conflicting paths.
- Update the project-worktree guide and focused sandbox/worktree ownership notes when
  the behavior lands.
