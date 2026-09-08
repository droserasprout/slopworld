# Sandboxing

SlopWorld is not production-grade security software. It gives more isolation than running
agents unsandboxed on your desktop, but that is the extent of the guarantee.

**Back up your data** before using SlopWorld.

## At a glance

Each agent runs inside a layered sandbox: systemd can apply resource limits, Bubblewrap
controls filesystem mounts, pasta provides the selected network namespace, and tmux owns
the terminal session. The sandbox is configured by project and agent settings, with
presets supplying mounts, environment, and capabilities.

The three network modes are:

- **none** — no connectivity.
- **private** — synthetic DNS with no port forwarding; egress is allowed but loopback is
  blocked.
- **host** — full host networking, including local services.

See [Configuring sandboxes](../guides/configuring-sandboxes.md) for preset syntax and
resolved settings, and the [Security model](../reference/security.md) for isolation
details, limits, and remaining exposure.

## Verifying the sandbox

When adding or editing an agent, visit the Preview tab to see the resolved sandbox
parameters. After spawning an agent, check the actual bwrap/pasta command line:

```sh
ps -ww -eo pid=,ppid=,user=,comm=,args= \
  | awk '$4 == "bwrap" || $4 == "pasta"'
```
