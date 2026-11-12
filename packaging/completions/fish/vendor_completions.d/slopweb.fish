# Print an optspec for argparse to handle cmd's options that are independent of any subcommand.
function __fish_slopweb_global_optspecs
    string join \n game= port= endpoint= url= token-file= cache= profile= sidecar prepare rebuild public-origin= interpreted aot aot-sdk= ensure-workload h/help V/version
end

function __fish_slopweb_needs_command
    # Figure out if the current invocation already has a command.
    set -l cmd (commandline -opc)
    set -e cmd[1]
    argparse -s (__fish_slopweb_global_optspecs) -- $cmd 2>/dev/null
    or return
    if set -q argv[1]
        # Also print the command, so this can be used to figure out what it is.
        echo $argv[1]
        return 1
    end
    return 0
end

function __fish_slopweb_using_subcommand
    set -l cmd (__fish_slopweb_needs_command)
    test -z "$cmd"
    and return 1
    contains -- $cmd[1] $argv
end

complete -c slopweb -n "__fish_slopweb_needs_command" -l game -r -F
complete -c slopweb -n "__fish_slopweb_needs_command" -l port -r
complete -c slopweb -n "__fish_slopweb_needs_command" -l endpoint -r
complete -c slopweb -n "__fish_slopweb_needs_command" -l url -r
complete -c slopweb -n "__fish_slopweb_needs_command" -l token-file -r -F
complete -c slopweb -n "__fish_slopweb_needs_command" -l cache -r -F
complete -c slopweb -n "__fish_slopweb_needs_command" -l profile -r -F
complete -c slopweb -n "__fish_slopweb_needs_command" -l public-origin -r
complete -c slopweb -n "__fish_slopweb_needs_command" -l aot-sdk -r -F
complete -c slopweb -n "__fish_slopweb_needs_command" -l sidecar
complete -c slopweb -n "__fish_slopweb_needs_command" -l prepare
complete -c slopweb -n "__fish_slopweb_needs_command" -l rebuild
complete -c slopweb -n "__fish_slopweb_needs_command" -l interpreted
complete -c slopweb -n "__fish_slopweb_needs_command" -l aot
complete -c slopweb -n "__fish_slopweb_needs_command" -l ensure-workload -d 'Provision a missing AOT workload for checkout development only'
complete -c slopweb -n "__fish_slopweb_needs_command" -s h -l help -d 'Print help'
complete -c slopweb -n "__fish_slopweb_needs_command" -s V -l version -d 'Print version'
complete -c slopweb -n "__fish_slopweb_needs_command" -f -a "paths" -d 'Print an absolute host path without preparing runtime state'
complete -c slopweb -n "__fish_slopweb_needs_command" -f -a "completions" -d 'Generate shell completions from the command tree'
complete -c slopweb -n "__fish_slopweb_needs_command" -f -a "check-fonts" -d 'Prepare native font delivery descriptors without starting a browser'
complete -c slopweb -n "__fish_slopweb_needs_command" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l game -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l endpoint -r
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l url -r
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l token-file -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l cache -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l profile -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -l sidecar
complete -c slopweb -n "__fish_slopweb_using_subcommand paths" -s h -l help -d 'Print help'
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l game -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l endpoint -r
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l url -r
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l token-file -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l cache -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l profile -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -l sidecar
complete -c slopweb -n "__fish_slopweb_using_subcommand completions" -s h -l help -d 'Print help'
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l game -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l endpoint -r
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l url -r
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l token-file -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l cache -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l profile -r -F
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -l sidecar
complete -c slopweb -n "__fish_slopweb_using_subcommand check-fonts" -s h -l help -d 'Print help'
complete -c slopweb -n "__fish_slopweb_using_subcommand help; and not __fish_seen_subcommand_from paths completions check-fonts help" -f -a "paths" -d 'Print an absolute host path without preparing runtime state'
complete -c slopweb -n "__fish_slopweb_using_subcommand help; and not __fish_seen_subcommand_from paths completions check-fonts help" -f -a "completions" -d 'Generate shell completions from the command tree'
complete -c slopweb -n "__fish_slopweb_using_subcommand help; and not __fish_seen_subcommand_from paths completions check-fonts help" -f -a "check-fonts" -d 'Prepare native font delivery descriptors without starting a browser'
complete -c slopweb -n "__fish_slopweb_using_subcommand help; and not __fish_seen_subcommand_from paths completions check-fonts help" -f -a "help" -d 'Print this message or the help of the given subcommand(s)'
