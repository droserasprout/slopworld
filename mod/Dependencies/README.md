# Mod runtime packages

`just mod-restore` restores AngleSharp 1.8.3, Markdig 0.18.3, Newtonsoft.Json
13.0.3, Tomlyn 0.19.0, and Google.Protobuf 3.36.1 with locked dependencies for
net10.0, then copies their runtime assemblies into `mod/Assemblies`.
The mod and game-free tests target .NET 10. RimWorld supplies framework assemblies,
including code-page encoding support.

`Runtime.csproj` owns package versions; `packages.lock.json` owns the resolved graph
and content hashes. To update packages, edit the project, run
`just deps-update-mod mod-restore`, review the lock and shipped DLL
changes, then run `just mod-restore` and `just mod-test`.
Json.NET supports the external SongRec integration. Daemon IPC uses Protobuf.

RimWorld's `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll` are build
references from the game installation. Keep `Private=false` on these references;
mod discovery must only see SlopWorld and its package dependencies.
`just deps-harmony` downloads the latest RimWorld Harmony release, verifies its
published SHA-256 digest, and atomically installs only `0Harmony.dll`.
Pass a destination directory as an argument to install elsewhere.

Canonical dependency licenses live in `licenses/runtime/`. `just generate-licenses`
stages them into `mod/About/ThirdPartyNotices/`; source installation and release
packaging use the canonical files directly.
