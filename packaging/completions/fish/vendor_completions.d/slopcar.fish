# Print an optspec for argparse to handle cmd's options that are independent of any subcommand.
function __fish_slopcar_global_optspecs
    string join \n h/help V/version
end

function __fish_slopcar_needs_command
    # Figure out if the current invocation already has a command.
    set -l cmd (commandline -opc)
    set -e cmd[1]
    argparse -s (__fish_slopcar_global_optspecs) -- $cmd 2>/dev/null
    or return
    if set -q argv[1]
        # Also print the command, so this can be used to figure out what it is.
        echo $argv[1]
        return 1
    end
    return 0
end

function __fish_slopcar_using_subcommand
    set -l cmd (__fish_slopcar_needs_command)
    test -z "$cmd"
    and return 1
    contains -- $cmd[1] $argv
end

complete -c slopcar -n "__fish_slopcar_needs_command" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_needs_command" -s V -l version -d 'Print version'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "completions" -d 'Print a shell completion script without Docker or container settings'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "paths" -d 'Print an application path without Docker or container settings'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "licenses" -d 'Show embedded dependency notices'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "build" -d 'Build the image from a checkout (default: current directory)'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "doctor" -d 'Check nested Bubblewrap, pasta and tmux'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "start" -d 'Create and start, or start the existing container'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "stop" -d 'Stop without deleting persistent state'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "restart" -d 'Restart the existing container'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "status" -d 'Show container and health state'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "logs" -d 'Forward all following arguments to Docker logs; use `help logs` for this help'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "shell" -d 'Open an interactive shell'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "rm" -d 'Remove only the container, preserving config and data'
complete -c slopcar -n "__fish_slopcar_needs_command" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopcar -n "__fish_slopcar_using_subcommand completions" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand paths" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand licenses" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand build" -l source -r -f -a "(__fish_complete_directories)"
complete -c slopcar -n "__fish_slopcar_using_subcommand build" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand doctor" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l workspace -d 'Mount an absolute workspace at the same path (repeatable)' -r -f -a "(__fish_complete_directories)"
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l credential-ro -d 'Mount a credential read-only (SOURCE=TARGET, repeatable)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l credential-rw -d 'Mount a credential read-write (SOURCE=TARGET, repeatable)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l port -d 'Host and daemon port (default: 7718)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l memory -d 'Memory ceiling (default: 8g)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l cpus -d 'CPU ceiling (default: 4)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -l pids-limit -d 'Process/thread ceiling (default: 4096)' -r
complete -c slopcar -n "__fish_slopcar_using_subcommand start" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand stop" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand restart" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand status" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand shell" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand rm" -s h -l help -d 'Print help'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "completions" -d 'Print a shell completion script without Docker or container settings'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "paths" -d 'Print an application path without Docker or container settings'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "licenses" -d 'Show embedded dependency notices'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "build" -d 'Build the image from a checkout (default: current directory)'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "doctor" -d 'Check nested Bubblewrap, pasta and tmux'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "start" -d 'Create and start, or start the existing container'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "stop" -d 'Stop without deleting persistent state'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "restart" -d 'Restart the existing container'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "status" -d 'Show container and health state'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "logs" -d 'Forward all following arguments to Docker logs; use `help logs` for this help'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "shell" -d 'Open an interactive shell'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "rm" -d 'Remove only the container, preserving config and data'
complete -c slopcar -n "__fish_slopcar_using_subcommand help; and not __fish_seen_subcommand_from completions paths licenses build doctor start stop restart status logs shell rm help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
