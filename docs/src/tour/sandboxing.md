# Sandboxing

Sandbox isolation does not guarantee security. See the [Security model](../reference/security.md)
for host-data exposure and remaining limits.

Projects supply workspace mounts. Agents select network and process settings;
presets add filesystem access, environment, and host capabilities. See
[Configuring projects](../guides/configuring-projects.md),
[Configuring agents](../guides/configuring-agents.md), and
[Configuring sandboxes](../guides/configuring-sandboxes.md).

## Before starting

Use the agent editor's Preview tab to inspect settings for the next start.
See [Preview and apply](../guides/configuring-agents.md#preview-and-apply).

## After starting

```sh
slopctl sandbox inspect AGENT
```

The sanitized saved plan describes the intended launch, not proof that startup
succeeded. Live observation may be unavailable. See
[Using slopctl](../guides/slopctl.md#diagnostics) for inspection details.
