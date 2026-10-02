# Requirements

## Choose an install mode

Use a [native Linux installation](install.md#linux), a [native macOS game with a
Linux sidecar](guides/macos.md), or a [standalone sidecar worker](guides/sidecar.md).
The mod requires RimWorld **1.6**.

## Native Linux

Core service and sandbox prerequisites are systemd user services/scopes,
Bubblewrap, `pasta` (from passt), and tmux. Run `make check-reqs` for the complete
host check, including required commands, libraries, an agent CLI, and optional tools.

Use the native game directory containing `RimWorldLinux` and
`RimWorldLinux_Data/Managed/`. Set `RIMWORLD` to that directory for Make build
and installation targets; the installed launcher accepts `--game` or `SLOPWORLD_GAME`.
See [Build from source](build.md) for the compiler and generation toolchain.

Managed worktree creation requires Landlock ABI 3 or newer; removal requires
Bubblewrap. See [Project worktrees](guides/project-worktrees.md).

## macOS and sidecar

Native macOS setup requires a RimWorld 1.6 app and Homebrew. Its setup target
installs GNU Make, the .NET SDK, and Docker Desktop; follow the [macOS guide](guides/macos.md).
Standalone sidecar setup requires Docker or Docker Desktop and a local repository
checkout; follow [Sidecar worker](guides/sidecar.md).

These workflows supply daemon runtime dependencies in the Linux container;
the host does not need the native Linux service dependencies.
