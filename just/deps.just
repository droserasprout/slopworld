set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Update dependency locks: all, tools, daemon, mod, sidecar or bench
[positional-arguments]
update what='all':
    python3 -m tools.recipes.dispatch deps-update "$@"

# Restore reviewed locks: all, tools, mod or dotnet
[positional-arguments]
restore what='all':
    python3 -m tools.recipes.dispatch deps-restore "$@"
