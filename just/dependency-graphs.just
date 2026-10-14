set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Update every dependency graph, then regenerate and stage notices
update: tools daemon sidecar bench mod
    "$JUST_CMD" --justfile "$JUST_ROOT" generate daemon-licenses generate sidecar-licenses generate licenses

# Update the tools dependency lock
tools:
    ${UV} lock --upgrade

# Update the daemon dependency lock
daemon:
    cd slopd && ${CARGO} update

# Update the sidecar dependency lock
sidecar:
    cd slopcar && ${CARGO} update

# Update the bench dependency lock
bench:
    cd bench/ipc/rust && ${CARGO} update

# Update the mod dependency locks
mod:
    ${DOTNET} restore mod/Dependencies/Runtime.csproj --force-evaluate -p:RestoreLockedMode=false
    ${DOTNET} restore mod/Source/SlopWorld/SlopWorld.csproj --force-evaluate -p:RestoreLockedMode=false
    ${DOTNET} restore mod/Tests/SlopWorld.Tests.csproj --force-evaluate -p:RestoreLockedMode=false
    ${DOTNET} restore bench/ipc/csharp/IpcBench.csproj --force-evaluate -p:RestoreLockedMode=false
