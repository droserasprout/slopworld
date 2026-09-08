# slopcar

`slopcar` packages the Linux daemon, tmux, Bubblewrap, pasta, and bundled agent CLIs
in a Debian container. The base supports amd64 and arm64; sandbox construction
uses the container's usr-merge layout at runtime.

For setup, workspace and credential mounts, client connection, and lifecycle, see
the [sidecar guide](../docs/src/guides/sidecar.md). Native macOS game setup is in
the [macOS guide](../docs/src/guides/macos.md).

## Outer isolation

The container runs as uid 1000 with every capability dropped, a read-only root filesystem and one
outer memory/CPU/PID budget. Nested user/mount/network namespaces need three deliberate exceptions:
the `seccomp.json` allowlist, Docker's `systempaths=unconfined`, and `/dev/net/tun`. `doctor`
exercises Bubblewrap and pasta together under exactly those flags. Bubblewrap cannot create
its nested user namespace under Docker's `no-new-privileges`, so that option is intentionally not
set.

The seccomp allowlist is the containers/common profile, extended only for hostname changes inside
Bubblewrap's private UTS namespace. It is included here because Docker's builtin profile blocks the
namespace syscalls Bubblewrap and pasta require. The container is not privileged and receives no
`CAP_SYS_ADMIN`.
