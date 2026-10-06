# Mod runtime packages

`just sync-mod` restores AngleSharp 1.8.3, Markdig 0.18.3, Newtonsoft.Json 13.0.3,
Tomlyn 0.19.0, and Google.Protobuf 3.36.1 with locked transitive dependencies for
net472, then copies
only runtime assemblies into `mod/Assemblies`. This target framework supports the
game's Unity Mono runtime. Markdig stays at its dependency-light Mono-compatible version.
The mod, game-free tests, coverage and benchmarks depend on this target.

`Runtime.csproj` owns package versions; `packages.lock.json` owns the resolved graph
and content hashes. To update packages, edit the project, run
`just MOD_DEPS_LOCKED=false sync-mod`, review the lock and shipped DLL changes, then
run `just sync-mod` and `just test-mod`. The parser DLLs and
`System.Text.Encoding.CodePages.dll` are generated and ignored.
The existing Protobuf runtime DLLs remain checked in. AngleSharp adds code-page
encoding support and raises the shared System.Memory/Unsafe dependencies; update
the checked-in transitive DLLs together with the runtime lock.

RimWorld and Unity assemblies come from the game installation and must never be copied
into the mod. `just fetch-harmony` downloads the latest RimWorld Harmony release,
verifies its published SHA-256 digest, and atomically installs only `0Harmony.dll`.
Pass a destination directory as an argument to install elsewhere.
The corresponding licenses live in `licenses/runtime/` and are staged into
`mod/About/ThirdPartyNotices/` by `just refresh-licenses`. Json.NET remains because the
external SongRec integration uses JSON. The daemon IPC client no longer uses Json.NET.
The standalone IPC benchmark runs the same Protobuf codec on Mono.
