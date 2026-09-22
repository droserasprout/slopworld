# Debugging from a sandbox

Check the running sandbox's network mode and `/proc/self/mountinfo` before interpreting
missing host resources. Preset changes take effect only after the agent restarts.

- In private network mode, `127.0.0.1` belongs to the sandbox. A failed connection there
  does not establish that the host daemon is down. Use the configured API URL when reachable.
- Ordinary private PID/proc views do not describe host processes. The `slopworld-debug`
  preset explicitly binds host `/proc` and `/sys`, so it has different diagnostic visibility.
- The debug preset binds daemon config/endpoint and journal files read-only and shares
  the session bus. `systemctl --user` therefore reaches host services; it is a host action.
  See [presets](daemon-presets.md) for the complete diagnostic capability boundary.
- With `slopworld-debug`, game-related work can run as the host against the host game install
  and profile.

`slopd.service` uses `Type=simple`: active status alone does not prove startup reached
`TcpListener::bind`. On the host, compare the listener with the startup journal:

```sh
ss -tlnp | grep 7717
journalctl --user -u slopd -n 30 --no-pager
```

Use the configured port if it differs. If there is no listener, inspect startup reconcile;
if it exists, check reachability and the endpoint URL/token before blaming the mod.
See [paths](ops-paths.md) and [diagnostics](ops-diagnostics.md).
