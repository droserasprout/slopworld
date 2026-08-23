# Sandboxing

## Important!

SlopWorld is not production-grade software, neither in security nor in any other sense! It provides more peace of mind than yolo'ing agents rawdog on your desktop, but that's all.

- BACKUP YOUR DATA!
- When adding or editing an agent, visit the "Preview" tab to see the sandbox params.
- After spawning an agent, run the following command to check the actual bwrap/pasta command line:

```shell
ps -ww -eo pid=,ppid=,user=,comm=,args= \
  | awk '$4 == "bwrap" || $4 == "pasta"'
```

## Big picture

- systemd-run for tmux sessions
- bwrap for binds
- pasta for networking
- authentication, tokens

## Configuration

### Sandbox presets

- binds
- devices
- private
- seeded

### Networking

Three modes.

- host. unlimited.
- private. blocks loopback. doesn't affect egress/ingress
- offline

If a project is limited to Private, you can't give Host to child agents.

### Process limits

