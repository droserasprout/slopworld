# Diagnostics

[Using slopctl](../docs/src/reference/slopctl.md) owns log and status operations;
[troubleshooting](../docs/src/help/troubleshooting.md) owns connection diagnosis.
Service active status does not establish listener readiness.

[Terminal latency](terminal-latency.md) owns diagnostic data boundaries and links
to capture/benchmark procedures. [CPU attribution](misc-cpu-threads.md) distinguishes
elapsed timers from process/thread CPU measurements.

[Session state](daemon-session-state.md) owns confirmed process-exit evidence and
reader recovery; [daemon sources](daemon-files.md) map CLI stream ownership.

The native service uses `Type=simple`; readiness requires a listener, not merely
active status. Startup logs separate config, Git warmup, recovery, reconciliation,
and bind failures. The unit supplies `SLOPD_LOG=slopd=info`; a shell export cannot
change its logging filter.
