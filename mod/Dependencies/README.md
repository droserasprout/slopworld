# Mod runtime packages

`just deps restore mod` restores AngleSharp 1.8.3, Markdig 0.18.3, Newtonsoft.Json
13.0.3, Tomlyn 0.19.0, Google.Protobuf 3.36.1, Lib.Harmony 2.4.2, and Tmds.DBus.Protocol 0.95.1 with locked dependencies for
net10.0, then copies their runtime assemblies into `mod/Assemblies`.
The mod and game-free tests target .NET 10. RimWorld supplies framework assemblies,
including code-page encoding support.

`Runtime.csproj` owns package versions; `packages.lock.json` owns the resolved graph
and content hashes. To update packages, edit the project, run
`just MOD_DEPS_LOCKED=false deps restore mod`, review the lock and shipped DLL
changes, then run `just deps restore mod` and `just test mod`.
Tmds.DBus.Protocol owns managed D-Bus transport for Linux idle inhibition; it adds no
transitive package assemblies for this target. Json.NET supports the external SongRec integration. Daemon IPC uses Protobuf.

RimWorld's `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll` are build
references from the game installation. Keep `Private=false` on these references;
mod discovery must only see SlopWorld and its package dependencies.
Harmony's .NET 10 assembly is restored from the pinned Lib.Harmony package by the
same runtime staging target. The game-free runtime test patches and restores a
managed method to verify the bundled assembly's detour support.

Canonical dependency licenses live in `licenses/runtime/`. `just generate licenses`
stages them into `mod/About/ThirdPartyNotices/`; source installation and release
packaging use the canonical files directly.
