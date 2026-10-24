# Print an optspec for argparse to handle cmd's options that are independent of any subcommand.
function __fish_slopctl_global_optspecs
    string join \n json sidecar endpoint= h/help V/version
end

function __fish_slopctl_needs_command
    # Figure out if the current invocation already has a command.
    set -l cmd (commandline -opc)
    set -e cmd[1]
    argparse -s (__fish_slopctl_global_optspecs) -- $cmd 2>/dev/null
    or return
    if set -q argv[1]
        # Also print the command, so this can be used to figure out what it is.
        echo $argv[1]
        return 1
    end
    return 0
end

function __fish_slopctl_using_subcommand
    set -l cmd (__fish_slopctl_needs_command)
    test -z "$cmd"
    and return 1
    contains -- $cmd[1] $argv
end

complete -c slopctl -n "__fish_slopctl_needs_command" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_needs_command" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_needs_command" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_needs_command" -s h -l help -d 'Print help (see more with \'--help\')'
complete -c slopctl -n "__fish_slopctl_needs_command" -s V -l version -d 'Print version'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "completions" -d 'Print a shell completion script without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "worker" -d 'Create task-owned workers from agent templates'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "agent" -d 'Create agents from the daemon\'s template catalog'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "template" -d 'Discover and inspect agent templates'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "worktree" -d 'Manage project worktrees; rename and remove require the root token'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "task" -d 'Manage delegated tasks; omit IDs when SLOPWORLD_TASK_ID is set'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "peers" -d 'List sessions visible to the caller, including host'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "status" -d 'Show caller, endpoint, daemon reachability, and pending task counts'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "sandbox" -d 'Inspect sanitized sandbox launch plans and live process trees'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "config" -d 'Read or persist daemon settings (requires the root token)'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "logs" -d 'Read local game and daemon logs, even while the daemon is down'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "paths" -d 'Print an absolute application path without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand completions" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand completions" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand completions" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand completions" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -f -a "spawn" -d 'Spawn a worker and deliver TASK; its worktree remains until removed'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and not __fish_seen_subcommand_from spawn help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l project -d 'Project that owns the agent' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l template -d 'Agent template name' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l worktree -d 'Attach an existing worktree' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l one-shot -d 'Remove the worker session on exit'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from help" -f -a "spawn" -d 'Spawn a worker and deliver TASK; its worktree remains until removed'
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from create help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l project -d 'Project that owns the agent' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l template -d 'Agent template name' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l start
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -f -a "list" -d 'List templates enabled for the caller; root sees the full catalog'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -f -a "show" -d 'Show one accessible template'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and not __fish_seen_subcommand_from list show help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from list" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from list" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from list" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from list" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from list" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from show" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from show" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from show" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from show" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from show" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from help" -f -a "list" -d 'List templates enabled for the caller; root sees the full catalog'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from help" -f -a "show" -d 'Show one accessible template'
complete -c slopctl -n "__fish_slopctl_using_subcommand template; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "list" -d 'List registered worktrees'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "create" -d 'Allocate a worktree or register an existing checkout'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "rename" -d 'Rename a registered worktree (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "remove" -d 'Remove a managed worktree or unregister an external checkout, keeping its files'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from list" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from list" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from list" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from list" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from list" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l name -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l base -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l path -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from create" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -l name -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from rename" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from remove" -l project -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from remove" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from remove" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from remove" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from remove" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "list" -d 'List registered worktrees'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "create" -d 'Allocate a worktree or register an existing checkout'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "rename" -d 'Rename a registered worktree (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "remove" -d 'Remove a managed worktree or unregister an external checkout, keeping its files'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "delegate" -d 'Send TASK to AGENT'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "list" -d 'List newest tasks first; terminal tasks are hidden unless --all or --status is used'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "show" -d 'Show one task by its exact ID'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "wait" -d 'Wait for a terminal result; report changes and a 30-second heartbeat to stderr'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "accept" -d 'Accept a queued task, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "progress" -d 'Set an accepted task to working, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "finish" -d 'Mark a task done, with an optional result'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "fail" -d 'Mark a task failed, with an optional reason'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "remove" -d 'Remove one terminal task'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "prune" -d 'Remove terminal tasks sent or received by the caller'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail remove prune help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from delegate" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from delegate" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from delegate" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from delegate" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l status -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l all
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l sent
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l received
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from list" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from show" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from show" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from show" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from show" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from wait" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from wait" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from wait" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from wait" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from accept" -l id -d 'Task ID (default: SLOPWORLD_TASK_ID)' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from accept" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from accept" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from accept" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from accept" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from progress" -l id -d 'Task ID (default: SLOPWORLD_TASK_ID)' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from progress" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from progress" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from progress" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from progress" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from finish" -l id -d 'Task ID (default: SLOPWORLD_TASK_ID)' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from finish" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from finish" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from finish" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from finish" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from fail" -l id -d 'Task ID (default: SLOPWORLD_TASK_ID)' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from fail" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from fail" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from fail" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from fail" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from remove" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from remove" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from remove" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from remove" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from prune" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from prune" -l include-active -d 'Also remove active tasks (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from prune" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from prune" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from prune" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "delegate" -d 'Send TASK to AGENT'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "list" -d 'List newest tasks first; terminal tasks are hidden unless --all or --status is used'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "show" -d 'Show one task by its exact ID'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "wait" -d 'Wait for a terminal result; report changes and a 30-second heartbeat to stderr'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "accept" -d 'Accept a queued task, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "progress" -d 'Set an accepted task to working, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "finish" -d 'Mark a task done, with an optional result'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "fail" -d 'Mark a task failed, with an optional reason'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "remove" -d 'Remove one terminal task'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "prune" -d 'Remove terminal tasks sent or received by the caller'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand peers" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand peers" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand peers" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand peers" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand status" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand status" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand status" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand status" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -f -a "inspect" -d 'Show the saved sanitized plan and live process tree, when available'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and not __fish_seen_subcommand_from inspect help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from inspect" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from inspect" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from inspect" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from inspect" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from help" -f -a "inspect" -d 'Show the saved sanitized plan and live process tree, when available'
complete -c slopctl -n "__fish_slopctl_using_subcommand sandbox; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -f -a "get" -d 'Read one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -f -a "set" -d 'Persist one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and not __fish_seen_subcommand_from get set help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from get" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from get" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from get" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from get" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from set" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from set" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from set" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from set" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from help" -f -a "get" -d 'Read one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from help" -f -a "set" -d 'Persist one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand config; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -s n -l lines -d 'Maximum recent lines per source' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -s f -l follow -d 'Keep reading appended lines'
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand logs" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand paths" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand paths" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand paths" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand paths" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "completions" -d 'Print a shell completion script without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "worker" -d 'Create task-owned workers from agent templates'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "agent" -d 'Create agents from the daemon\'s template catalog'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "template" -d 'Discover and inspect agent templates'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "worktree" -d 'Manage project worktrees; rename and remove require the root token'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "task" -d 'Manage delegated tasks; omit IDs when SLOPWORLD_TASK_ID is set'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "peers" -d 'List sessions visible to the caller, including host'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "status" -d 'Show caller, endpoint, daemon reachability, and pending task counts'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "sandbox" -d 'Inspect sanitized sandbox launch plans and live process trees'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "config" -d 'Read or persist daemon settings (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "logs" -d 'Read local game and daemon logs, even while the daemon is down'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "paths" -d 'Print an absolute application path without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent template worktree task peers status sandbox config logs paths help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worker" -f -a "spawn" -d 'Spawn a worker and deliver TASK; its worktree remains until removed'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from template" -f -a "list" -d 'List templates enabled for the caller; root sees the full catalog'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from template" -f -a "show" -d 'Show one accessible template'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "list" -d 'List registered worktrees'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "create" -d 'Allocate a worktree or register an existing checkout'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "rename" -d 'Rename a registered worktree (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "remove" -d 'Remove a managed worktree or unregister an external checkout, keeping its files'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "delegate" -d 'Send TASK to AGENT'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "list" -d 'List newest tasks first; terminal tasks are hidden unless --all or --status is used'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "show" -d 'Show one task by its exact ID'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "wait" -d 'Wait for a terminal result; report changes and a 30-second heartbeat to stderr'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "accept" -d 'Accept a queued task, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "progress" -d 'Set an accepted task to working, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "finish" -d 'Mark a task done, with an optional result'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "fail" -d 'Mark a task failed, with an optional reason'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "remove" -d 'Remove one terminal task'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "prune" -d 'Remove terminal tasks sent or received by the caller'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from sandbox" -f -a "inspect" -d 'Show the saved sanitized plan and live process tree, when available'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from config" -f -a "get" -d 'Read one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from config" -f -a "set" -d 'Persist one setting'
