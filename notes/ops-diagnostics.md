# Diagnostics

[Using slopctl](../docs/src/guides/slopctl.md) owns log and status operations;
[troubleshooting](../docs/src/reference/troubleshooting.md) owns connection diagnosis.
Service active status does not establish listener readiness.

[Terminal latency](terminal-latency.md) owns diagnostic data boundaries and links
to capture/benchmark procedures. [CPU attribution](misc-cpu-threads.md) distinguishes
elapsed timers from process/thread CPU measurements.

[Session state](daemon-session-state.md) owns confirmed process-exit evidence and
reader recovery; [daemon sources](daemon-files.md) map CLI stream ownership.
