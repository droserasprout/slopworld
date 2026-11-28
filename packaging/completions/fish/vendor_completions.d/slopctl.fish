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
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "agent" -d 'Create agents and control their sessions'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "project" -d 'Inspect configured projects (requires the root token)'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "board" -d 'Collaborate on project boards and archives'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "template" -d 'Discover and inspect agent templates'
complete -c slopctl -n "__fish_slopctl_needs_command" -f -a "worktree" -d 'Manage project worktrees; mutations require worktree management permission'
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
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l card -r
complete -c slopctl -n "__fish_slopctl_using_subcommand worker; and __fish_seen_subcommand_from spawn" -l name -d 'Exact worker name; omitted names are generated from the project or caller' -r
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
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "start" -d 'Start an existing agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "resume" -d 'Resume an agent from its saved state'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "restart" -d 'Restart an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "stop" -d 'Stop an agent session; one-shot workers are removed on stop'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "remove" -d 'Remove an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and not __fish_seen_subcommand_from start resume restart stop remove create help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from start" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from start" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from start" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from start" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from resume" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from resume" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from resume" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from resume" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from restart" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from restart" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from restart" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from restart" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from stop" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from stop" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from stop" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from stop" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from remove" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from remove" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from remove" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from remove" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l project -d 'Project that owns the agent' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l template -d 'Agent template name' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l start
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from create" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "start" -d 'Start an existing agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "resume" -d 'Resume an agent from its saved state'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "restart" -d 'Restart an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "stop" -d 'Stop an agent session; one-shot workers are removed on stop'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "remove" -d 'Remove an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand agent; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -f -a "list" -d 'List configured project names'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -f -a "show" -d 'Show a project\'s full configuration'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and not __fish_seen_subcommand_from list show help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from list" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from list" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from list" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from list" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from show" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from show" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from show" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from show" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from help" -f -a "list" -d 'List configured project names'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from help" -f -a "show" -d 'Show a project\'s full configuration'
complete -c slopctl -n "__fish_slopctl_using_subcommand project; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "list" -d 'List active and archived boards with their stable IDs'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "show" -d 'Show ordered columns, cards, assignments, comments and resource links'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "set-prefix"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "edit-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "remove-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "remove-activity"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "add-column" -d 'Append a column, or insert it before another column'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "rename-column"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "move-column" -d 'Reorder a column; omit --before to append'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "remove-column" -d 'Remove an empty column'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "add-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "move-card" -d 'Move an existing card between columns of the same board'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "remove-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "title"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "describe"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "assign" -d 'Set the sole assignee; omit name to assign yourself'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "unassign"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "link"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "unlink"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "delete-archive" -d 'Permanently delete an archived board and its remaining cards'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and not __fish_seen_subcommand_from list show set-prefix edit-comment remove-comment remove-activity add-column rename-column move-column remove-column add-card move-card remove-card title describe assign unassign comment link unlink delete-archive help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from list" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from list" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from list" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from list" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from show" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from show" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from show" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from show" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from set-prefix" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from set-prefix" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from set-prefix" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from set-prefix" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from edit-comment" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from edit-comment" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from edit-comment" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from edit-comment" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-comment" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-comment" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-comment" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-comment" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-activity" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-activity" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-activity" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-activity" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-column" -l before -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-column" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-column" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-column" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-column" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from rename-column" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from rename-column" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from rename-column" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from rename-column" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-column" -l before -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-column" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-column" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-column" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-column" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-column" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-column" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-column" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-column" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-card" -l before -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-card" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-card" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-card" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from add-card" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-card" -l before -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-card" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-card" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-card" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from move-card" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-card" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-card" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-card" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from remove-card" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from title" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from title" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from title" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from title" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from describe" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from describe" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from describe" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from describe" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from assign" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from assign" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from assign" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from assign" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unassign" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unassign" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unassign" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unassign" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from comment" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from comment" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from comment" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from comment" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from link" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from link" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from link" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from link" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unlink" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unlink" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unlink" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from unlink" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from delete-archive" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from delete-archive" -l confirm
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from delete-archive" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from delete-archive" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from delete-archive" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "list" -d 'List active and archived boards with their stable IDs'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "show" -d 'Show ordered columns, cards, assignments, comments and resource links'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "set-prefix"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "edit-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "remove-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "remove-activity"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "add-column" -d 'Append a column, or insert it before another column'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "rename-column"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "move-column" -d 'Reorder a column; omit --before to append'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "remove-column" -d 'Remove an empty column'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "add-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "move-card" -d 'Move an existing card between columns of the same board'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "remove-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "title"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "describe"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "assign" -d 'Set the sole assignee; omit name to assign yourself'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "unassign"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "link"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "unlink"
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "delete-archive" -d 'Permanently delete an archived board and its remaining cards'
complete -c slopctl -n "__fish_slopctl_using_subcommand board; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
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
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and not __fish_seen_subcommand_from list create rename remove help" -f -a "rename" -d 'Rename a registered worktree (requires worktree management permission)'
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
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "rename" -d 'Rename a registered worktree (requires worktree management permission)'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "remove" -d 'Remove a managed worktree or unregister an external checkout, keeping its files'
complete -c slopctl -n "__fish_slopctl_using_subcommand worktree; and __fish_seen_subcommand_from help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "delegate" -d 'Send TASK to AGENT'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "list" -d 'List newest tasks first; terminal tasks are hidden unless --all or --status is used'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "show" -d 'Show one task by its exact ID'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "wait" -d 'Wait for a terminal result; report changes and a 30-second heartbeat to stderr'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "accept" -d 'Accept a queued task, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "progress" -d 'Set an accepted task to working, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "finish" -d 'Mark a task done, with an optional result'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "fail" -d 'Mark a task failed, with an optional reason'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "followup" -d 'Send a follow-up prompt to the current worker run (requires follow-up permission)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "cancel" -d 'Cancel a task without stopping its worker'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "remove" -d 'Remove one terminal task'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "prune" -d 'Remove terminal tasks sent or received by the caller'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and not __fish_seen_subcommand_from delegate list show wait accept progress finish fail followup cancel remove prune help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from delegate" -l card -r
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
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from followup" -l id -d 'Task ID (default: SLOPWORLD_TASK_ID)' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from followup" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from followup" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from followup" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from followup" -s h -l help -d 'Print help'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from cancel" -l endpoint -d 'Select endpoints/NAME.toml and its automatic game profile' -r
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from cancel" -l json -d 'Print JSON instead of human-readable output'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from cancel" -l sidecar -d 'Select the sidecar endpoint and log sources'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from cancel" -s h -l help -d 'Print help'
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
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "followup" -d 'Send a follow-up prompt to the current worker run (requires follow-up permission)'
complete -c slopctl -n "__fish_slopctl_using_subcommand task; and __fish_seen_subcommand_from help" -f -a "cancel" -d 'Cancel a task without stopping its worker'
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
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "completions" -d 'Print a shell completion script without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "worker" -d 'Create task-owned workers from agent templates'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "agent" -d 'Create agents and control their sessions'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "project" -d 'Inspect configured projects (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "board" -d 'Collaborate on project boards and archives'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "template" -d 'Discover and inspect agent templates'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "worktree" -d 'Manage project worktrees; mutations require worktree management permission'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "task" -d 'Manage delegated tasks; omit IDs when SLOPWORLD_TASK_ID is set'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "peers" -d 'List sessions visible to the caller, including host'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "status" -d 'Show caller, endpoint, daemon reachability, and pending task counts'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "sandbox" -d 'Inspect sanitized sandbox launch plans and live process trees'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "config" -d 'Read or persist daemon settings (requires the root token)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "logs" -d 'Read local game and daemon logs, even while the daemon is down'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "paths" -d 'Print an absolute application path without contacting the daemon'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and not __fish_seen_subcommand_from completions worker agent project board template worktree task peers status sandbox config logs paths help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worker" -f -a "spawn" -d 'Spawn a worker and deliver TASK; its worktree remains until removed'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "start" -d 'Start an existing agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "resume" -d 'Resume an agent from its saved state'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "restart" -d 'Restart an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "stop" -d 'Stop an agent session; one-shot workers are removed on stop'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "remove" -d 'Remove an agent session'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from agent" -f -a "create" -d 'Create an agent, stopped unless --start is supplied'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from project" -f -a "list" -d 'List configured project names'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from project" -f -a "show" -d 'Show a project\'s full configuration'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "list" -d 'List active and archived boards with their stable IDs'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "show" -d 'Show ordered columns, cards, assignments, comments and resource links'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "set-prefix"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "edit-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "remove-comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "remove-activity"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "add-column" -d 'Append a column, or insert it before another column'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "rename-column"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "move-column" -d 'Reorder a column; omit --before to append'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "remove-column" -d 'Remove an empty column'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "add-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "move-card" -d 'Move an existing card between columns of the same board'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "remove-card"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "title"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "describe"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "assign" -d 'Set the sole assignee; omit name to assign yourself'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "unassign"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "comment"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "link"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "unlink"
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from board" -f -a "delete-archive" -d 'Permanently delete an archived board and its remaining cards'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from template" -f -a "list" -d 'List templates enabled for the caller; root sees the full catalog'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from template" -f -a "show" -d 'Show one accessible template'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "list" -d 'List registered worktrees'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "create" -d 'Allocate a worktree or register an existing checkout'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "rename" -d 'Rename a registered worktree (requires worktree management permission)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from worktree" -f -a "remove" -d 'Remove a managed worktree or unregister an external checkout, keeping its files'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "delegate" -d 'Send TASK to AGENT'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "list" -d 'List newest tasks first; terminal tasks are hidden unless --all or --status is used'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "show" -d 'Show one task by its exact ID'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "wait" -d 'Wait for a terminal result; report changes and a 30-second heartbeat to stderr'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "accept" -d 'Accept a queued task, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "progress" -d 'Set an accepted task to working, with an optional note'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "finish" -d 'Mark a task done, with an optional result'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "fail" -d 'Mark a task failed, with an optional reason'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "followup" -d 'Send a follow-up prompt to the current worker run (requires follow-up permission)'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "cancel" -d 'Cancel a task without stopping its worker'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "remove" -d 'Remove one terminal task'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from task" -f -a "prune" -d 'Remove terminal tasks sent or received by the caller'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from sandbox" -f -a "inspect" -d 'Show the saved sanitized plan and live process tree, when available'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from config" -f -a "get" -d 'Read one setting'
complete -c slopctl -n "__fish_slopctl_using_subcommand help; and __fish_seen_subcommand_from config" -f -a "set" -d 'Persist one setting'
