# slopcar

`slopcar` runs the Linux daemon, tmux, Bubblewrap, pasta and the four shipped agent CLIs in
one Debian trixie container. RimWorld and the mod remain native on macOS.

The Debian base is multiarch, so Docker Desktop builds and runs the image natively on amd64 and
Apple Silicon (arm64). slopd's sandbox skeleton reads the host's own
usr-merge layout at runtime, so the nested Bubblewrap works the same on either architecture.

The agent CLIs are still baked into the image for now; running host-native (Darwin) agents is
planned to replace that.

Build and prove the nested sandbox before starting it:

```sh
./slopcar/slopcar build
./slopcar/slopcar doctor
```

Create the container with each approved workspace named separately. Paths stay identical on
the Mac and in the container because they also cross the SlopWorld wire:

```sh
./slopcar/slopcar start --workspace "$HOME/git"
```

The first start creates `~/.config/slopworld/config.toml` with a random token, publishes only
`127.0.0.1:7718`, and persists config plus session state under the usual SlopWorld directories.
Subsequent `start` calls reuse the stopped container. Run `rm`, then `start` again to change
mounts or the outer resource budget; persistent state is not removed.

Credentials are opt-in named mounts. Codex can be seeded without exposing its whole home:

```sh
./slopcar/slopcar start \
  --workspace "$HOME/git" \
  --credential-ro "$HOME/.codex/auth.json=/home/slop/.codex/auth.json"
```

Claude's rotating credential file needs a read-write mount:

```sh
--credential-rw "$HOME/.claude/.credentials.json=/home/slop/.claude/.credentials.json"
```

The launcher refuses `/`, the whole home, the Docker socket/configuration, and paths overlapping
SlopWorld's token or private session state. It never mounts the Docker socket.

## Pointing the mod at the sidecar

The daemon writes `endpoint.toml` (url `http://127.0.0.1:7718` for its wildcard bind, plus the
token) into the sidecar's config dir, and that dir is bind-mounted back to the host — so the file
the mod reads is ready the moment the container is up. The mod discovers the daemon from
`$SLOPD_ENDPOINT` first, so any client points at the sidecar by exporting that path; no native
`slopd` has to run.

For the Linux dev game, `make run-slopcar` launches RimWorld into
`…/slopworld-car/profile`, kept wholly apart from the native profile but inside the state root the
debug sandbox already mounts, with `SLOPD_ENDPOINT` set to the sidecar's descriptor. Start the
sidecar first. Override `SLOPCAR_CONFIG` if you started it with a non-default
`SLOPCAR_CONFIG_DIR`, or `SLOPCAR_PROFILE` to name a different save folder. The separate profile
and profile-keyed launcher lock let this game run beside a native session.

For a native macOS game and mod, use the Makefile workflow from the repository root:

```sh
make mac-setup
open -a Docker
make mac
```

`mac-install` compiles against `RimWorldMac.app`'s managed assemblies and installs the mod;
`mac-run` starts the sidecar and launches the game into its separate profile. Override
`MAC_RIMWORLD` for a non-Steam install or `SLOPCAR_WORKSPACE` for the roots agents may access.

### Running beside a native daemon

A native `slopd` (the systemd user service) holds `127.0.0.1:7717`; the sidecar's default `7718`
keeps the two endpoints separate. To use another port, give the sidecar its own port and its own
config/data dirs — the daemon binds that port inside the container and writes it into
`endpoint.toml`, so the descriptor stays correct:

```sh
SLOPCAR_CONFIG_DIR=~/.config/slopworld-car SLOPCAR_DATA_DIR=~/.local/share/slopworld-car \
  ./slopcar/slopcar start --workspace "$HOME/git" --port 7719
make run-slopcar SLOPCAR_CONFIG=~/.config/slopworld-car
```

`--port` (or `SLOPCAR_PORT`) fixes both the host publish and the daemon's bind. It takes effect when
the config is seeded; reusing a config dir with a different port is refused.

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
