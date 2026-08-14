# Known limitations

## Private networking across a network change

Private agents run through a long-lived `pasta` network namespace. An established
TCP connection can remain stuck when the host changes uplinks (for example, Wi-Fi
to Ethernet), even though DNS and new connections work again. This is a stale
connection in the network path, not private Codex state corruption.

After changing networks, use **Agent: Restart** for affected agents. Restarting
recreates `pasta` and the agent process while preserving private state. Do not use
**Reset Storage** for this problem; reset is for damaged or intentionally fresh
tool state.

Automatic recovery is deliberately not attempted. Deciding that an agent is idle
can kill a local command, and resuming or restoring input can duplicate a request
that completed remotely while its response was lost. Supported agents also differ
in their resume behavior.
