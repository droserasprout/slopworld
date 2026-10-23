# Mod runtime packages

`just mod-restore` restores AngleSharp 1.8.3, Markdig 0.18.3, Newtonsoft.Json 13.0.3,
Tomlyn 0.19.0, and Google.Protobuf 3.36.1 with locked transitive dependencies for
net472, then copies
only runtime assemblies into `mod/Assemblies`. This target framework supports the
game's Unity Mono runtime. Markdig stays at its dependency-light Mono-compatible version.
The mod, game-free tests, coverage and benchmarks depend on this target.

`Runtime.csproj` owns package versions; `packages.lock.json` owns the resolved graph
and content hashes. To update packages, edit the project, run
`just deps-update-mod mod-restore`, review the lock and shipped DLL changes, then
run `just mod-restore` and `just mod-test`. The parser DLLs and
`System.Text.Encoding.CodePages.dll` are generated and ignored.
The existing Protobuf runtime DLLs remain checked in. AngleSharp adds code-page
encoding support and raises the shared System.Memory/Unsafe dependencies; update
the checked-in transitive DLLs together with the runtime lock.

RimWorld and Unity references come from the game installation by default, or the
main mod project's pinned RimRef package for `just mod-build rimref`. Neither
source may be copied into the mod. RimRef is a compile-only dependency and is not
part of this runtime package graph. `just deps-harmony` downloads the latest RimWorld Harmony release,
verifies its published SHA-256 digest, and atomically installs only the RimWorld 1.6
`0Harmony.dll` from the versioned archive.
Pass a destination directory as an argument to install elsewhere.
The corresponding licenses live in `licenses/runtime/` and are staged into
`mod/About/ThirdPartyNotices/` by `just generate-licenses`. Json.NET remains because the
external SongRec integration uses JSON. The daemon IPC client no longer uses Json.NET.
The standalone IPC benchmark runs the same Protobuf codec on Mono.
