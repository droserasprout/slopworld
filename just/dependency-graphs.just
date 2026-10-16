# Update the tools dependency lock
[group('deps-update')]
deps-update-tools:
    uv lock --upgrade

# Update the daemon dependency lock
[group('deps-update')]
deps-update-daemon:
    cd slopd && cargo update

# Update the sidecar dependency lock
[group('deps-update')]
deps-update-sidecar:
    cd slopcar && cargo update

# Update the bench dependency lock
[group('deps-update')]
deps-update-bench:
    cd bench/ipc/rust && cargo update

# Update the mod dependency locks
[group('deps-update')]
deps-update-mod:
    dotnet restore mod/Dependencies/Runtime.csproj --force-evaluate -p:RestoreLockedMode=false
    dotnet restore mod/Source/SlopWorld/SlopWorld.csproj --force-evaluate -p:RestoreLockedMode=false
    dotnet restore mod/Tests/SlopWorld.Tests.csproj --force-evaluate -p:RestoreLockedMode=false
    dotnet restore bench/ipc/csharp/IpcBench.csproj --force-evaluate -p:RestoreLockedMode=false
