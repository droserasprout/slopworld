# slopcar

`slopcar` packages the Linux daemon, tmux, Bubblewrap, pasta, and bundled agent CLIs
in a Debian container.
The base supports amd64 and arm64.
Sandbox construction uses the container's usr-merge layout at runtime.

For setup, workspace and credential mounts, client connection, and lifecycle, see
the [sidecar guide](../docs/src/guides/sidecar.md). Native macOS game setup is in
the [macOS guide](../docs/src/guides/macos.md).

## Outer isolation

The container runs as UID 1000 with no capabilities and a read-only root filesystem.
One outer set of memory, CPU, and PID limits applies to the container.
Nested user, mount, and network namespaces require three deliberate exceptions:

- The `seccomp.json` allowlist.
- Docker's `systempaths=unconfined`.
- `/dev/net/tun`.

`doctor` checks Bubblewrap and pasta together with exactly those flags. Bubblewrap cannot create
its nested user namespace under Docker's `no-new-privileges`, so that option is intentionally not
set.

The seccomp allowlist is the containers/common profile, extended only for hostname changes inside
Bubblewrap's private UTS namespace. It is included here because Docker's builtin profile blocks the
namespace syscalls Bubblewrap and pasta require. The container is not privileged and receives no
`CAP_SYS_ADMIN`.
