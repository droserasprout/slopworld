# Diagnostics

[slopctl](../docs/src/reference/slopctl.md) owns log/status commands;
[troubleshooting](../docs/src/resources/troubleshooting.md) owns connection diagnosis.
[Terminal latency](terminal-latency.md) explains trace and benchmark limits;
[capture](daemon-terminal-capture.md) owns exit evidence and reader recovery.

The `slopd` systemd service uses `Type=simple`: readiness requires a listener, not active status.
Its unit sets `SLOPD_LOG`; shell exports do not change the service filter. Sidecar logs
format both Docker output streams, including stderr in JSON and combined output.

Daemon CPU mixes Tokio capture/input work and dedicated audio threads. A busy runtime
thread cannot identify an agent. Tmux, control clients, and sandbox process trees have
separate accounting. Pane PIDs identify agent trees; configured resource limits add
systemd scopes that account for descendants. Unrestricted sessions lack that boundary.
The game mixes mod and vanilla work on Unity's main thread.

Trace timers measure elapsed time, not thread CPU; counters measure work. Use CPU
samples and stacks for attribution. [Redeploy](daemon-redeploy.md) owns service
lifetimes; [sandbox](sandbox-isolation.md) owns resource limits.

Audio health warnings cover CPAL-reported output errors and the daemon's source queue.
PipeWire graph deadline misses can remain invisible to both; compare `pw-profiler`
or `pw-top` counters with the daemon logs before ruling out playback glitches.
Capture output-connection lifetime and suspend/resume when comparing runs: a fresh
connection can behave differently from a long-lived one under the same graph quantum.
The ignored `compares_sound_server_bridges` audio test drives both ALSA server paths
with muted OST through the production format negotiation and queue; set
`SLOPD_AUDIO_TEST_SOURCE` to reproduce a particular stream format. Its direct PipeWire
output is diagnostic-only and bypasses the supported-device policy. External graph
counters remain necessary.
