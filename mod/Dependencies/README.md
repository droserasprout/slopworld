# Protobuf runtime

`make protobuf-deps` restores Google.Protobuf 3.36.1 and its locked transitive dependencies
for net472, then copies the runtime DLLs into `mod/Assemblies`. This target framework supports
the game's Unity Mono runtime. The standalone IPC benchmark runs the same codec on Mono.

The shipped System.Memory, System.Buffers, System.Numerics.Vectors and
System.Runtime.CompilerServices.Unsafe versions come from `packages.lock.json`.
The corresponding licenses are beside the assemblies. Json.NET remains because the
external SongRec integration uses JSON. The daemon IPC client no longer uses Json.NET.
