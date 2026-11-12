complete -c slopweb -f
complete -c slopweb -n '__fish_use_subcommand' -a 'paths completions'
complete -c slopweb -s h
complete -c slopweb -l help
complete -c slopweb -s V
complete -c slopweb -l version
complete -c slopweb -l game -r -F
complete -c slopweb -l port -r -F
complete -c slopweb -l endpoint -r -F
complete -c slopweb -l url -r -F
complete -c slopweb -l token-file -r -F
complete -c slopweb -l cache -r -F
complete -c slopweb -l profile -r -F
complete -c slopweb -l sidecar
complete -c slopweb -l prepare
complete -c slopweb -l rebuild
complete -c slopweb -l public-origin -r -F
complete -c slopweb -l interpreted
complete -c slopweb -l aot
complete -c slopweb -l aot-sdk -r -F
complete -c slopweb -n '__fish_seen_subcommand_from paths' -a 'config data cache endpoint profile game-log web-cache game'
complete -c slopweb -n '__fish_seen_subcommand_from completions' -a 'bash zsh fish'
