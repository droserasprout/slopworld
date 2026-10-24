# Print an optspec for argparse to handle cmd's options that are independent of any subcommand.
function __fish_slopworld_global_optspecs
    string join \n game= game-exe= working-dir= mods= profile= endpoint= sidecar print no-window-fix h/help V/version
end

function __fish_slopworld_needs_command
    # Figure out if the current invocation already has a command.
    set -l cmd (commandline -opc)
    set -e cmd[1]
    argparse -s (__fish_slopworld_global_optspecs) -- $cmd 2>/dev/null
    or return
    if set -q argv[1]
        # Also print the command, so this can be used to figure out what it is.
        echo $argv[1]
        return 1
    end
    return 0
end

function __fish_slopworld_using_subcommand
    set -l cmd (__fish_slopworld_needs_command)
    test -z "$cmd"
    and return 1
    contains -- $cmd[1] $argv
end

complete -c slopworld -n "__fish_slopworld_needs_command" -l game -d 'RimWorld install (default: SLOPWORLD_GAME, game.toml, usual locations)' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_needs_command" -l game-exe -d 'Explicit executable for native macOS app bundles' -r -F
complete -c slopworld -n "__fish_slopworld_needs_command" -l working-dir -d 'Working directory for the game process' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_needs_command" -l mods -d 'Mod directory to validate for a non-Linux game layout' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_needs_command" -l profile -d 'Save data folder (default: SLOPWORLD_PROFILE, selected connection profile)' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_needs_command" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_needs_command" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_needs_command" -l print -d 'Print game argv without launching or creating files'
complete -c slopworld -n "__fish_slopworld_needs_command" -l no-window-fix -d 'Omit default X11/OpenGL window arguments'
complete -c slopworld -n "__fish_slopworld_needs_command" -s h -l help -d 'Print help (see more with \'--help\')'
complete -c slopworld -n "__fish_slopworld_needs_command" -s V -l version -d 'Print version'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "completions" -d 'Print a shell completion script without loading game or profile state'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "connect" -d 'Verify and save a remote peer; prompt for its key if needed'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "endpoints" -d 'List saved endpoint names and URLs'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "paths" -d 'Print an application path without contacting the daemon'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "profile" -d 'Initialize or reset a profile without starting the game'
complete -c slopworld -n "__fish_slopworld_needs_command" -f -a "mod" -d 'Install or remove the SlopWorld mod'
complete -c slopworld -n "__fish_slopworld_using_subcommand completions" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand completions" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand completions" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand connect" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand connect" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand connect" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand endpoints" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand endpoints" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand endpoints" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand paths" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand paths" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand paths" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and not __fish_seen_subcommand_from init reset" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and not __fish_seen_subcommand_from init reset" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and not __fish_seen_subcommand_from init reset" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and not __fish_seen_subcommand_from init reset" -f -a "init" -d 'Create missing profile files, preserving existing settings and mod choices'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and not __fish_seen_subcommand_from init reset" -f -a "reset" -d 'Replace the mod list, preserving saves and other settings'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from init" -l profile -d 'Save data folder (default: SLOPWORLD_PROFILE, selected connection profile)' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from init" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from init" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from init" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from reset" -l profile -d 'Save data folder (default: SLOPWORLD_PROFILE, selected connection profile)' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from reset" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from reset" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand profile; and __fish_seen_subcommand_from reset" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and not __fish_seen_subcommand_from install uninstall" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and not __fish_seen_subcommand_from install uninstall" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and not __fish_seen_subcommand_from install uninstall" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and not __fish_seen_subcommand_from install uninstall" -f -a "install" -d 'Atomically replace the mod after copying to a temporary sibling directory'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and not __fish_seen_subcommand_from install uninstall" -f -a "uninstall" -d 'Remove only the SlopWorld directory inside Mods'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -l source -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -l game -d 'Directory containing Mods' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -l mods -d 'Exact Mods directory' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from install" -s h -l help -d 'Print help'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from uninstall" -l game -d 'Directory containing Mods' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from uninstall" -l mods -d 'Exact Mods directory' -r -f -a "(__fish_complete_directories)"
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from uninstall" -l endpoint -d 'Saved connection (default: local)' -r
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from uninstall" -l sidecar -d 'Use sidecar paths and UI defaults'
complete -c slopworld -n "__fish_slopworld_using_subcommand mod; and __fish_seen_subcommand_from uninstall" -s h -l help -d 'Print help'
