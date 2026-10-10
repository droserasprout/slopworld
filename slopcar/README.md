# slopcar

`slopcar` is a native Rust host launcher for a Debian container containing the
Linux daemon, tmux, Bubblewrap, pasta, and bundled agent CLIs.
The container base supports amd64 and arm64.
The independent `slopcar/Cargo.toml` crate builds on Linux and macOS without
the daemon's Linux/audio/protobuf dependencies. `just sidecar` builds it;
`just install-sidecar` installs it. `just test-sidecar` runs game-free tests.
`just refresh-sidecar-licenses` updates its dependency notices after lock changes;
`slopcar licenses` prints the notices embedded in the executable.

`src/lifecycle.rs` owns Docker lifecycle and security flags; `src/docker.rs`
owns subprocess transport. `src/mounts.rs` owns mount validation and
`src/config.rs` seeds host config without overwriting existing files.
The host executable embeds `seccomp.json`; the Dockerfile and container-side
entrypoint/doctor remain image inputs. Only image builds require a checkout,
selected with `build --source PATH` (default: current directory).
Sandbox construction uses the container's usr-merge layout at runtime.

For setup, workspace and credential mounts, client connection, and lifecycle, see
the [sidecar guide](../docs/src/deployment/sidecar.md). Native macOS game setup is in
the [macOS guide](../docs/src/deployment/macos.md).

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
