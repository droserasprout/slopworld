# RimWorld / SlopWorld browser compatibility

This directory owns offline assembly inspection and a reusable managed capability
probe. It does not own the browser runtime, graphics implementation, host server,
or `slopd`. Game files remain in the user's installation; generated metadata stays
in ignored `out/`. No game binaries are copied into tracked paths.

Run from the repository root:

```sh
just mod-build
just --justfile experiments/browser/compat/justfile summary
just --justfile experiments/browser/compat/justfile host-probe
just --justfile experiments/browser/compat/justfile browser-probe
just --justfile experiments/browser/compat/justfile trace \
  "$HOME/.local/share/Steam/steamapps/common/RimWorld/RimWorld.dll" \
  'RimWorldWaylith.Program::Main'
```

`RIMWORLD` overrides the default game directory. `inspect GAME_DIRECTORY` also
accepts a directory explicitly. Inspection uses .NET's PE metadata reader and does
not load target assemblies or run static constructors. The SDK comes from the
repository's `global.json`; no extra inspection packages are needed.

On a clean checkout, `just mod-build` builds `mod/Assemblies/SlopWorld.dll`
against the installed game and restores the mod dependencies. The browser build
requires that DLL and reports this prerequisite if it is absent. It clears its
staging directory before copying framework files and the existing mod assembly.

`out/inventory.json` includes assembly hashes, references, native imports, and IL
call sites classified as dynamic code, filesystem, threads/blocking, native
networking, or host process/native loading. `out/summary.json` reduces this to
counts, identities, and selected game call sites. These are candidates for review,
not reachability analysis: opcode comparisons count as dynamic-code references,
OS-specific dead branches are included, indirect native function pointers are not
counted as P/Invokes, generic type specifications are shown as metadata tokens,
and most framework assemblies are excluded. Counts are not a count of blockers.

The small 2026-11-10 result summary is in
[`evidence/result.json`](evidence/result.json). Selected assembly SHA-256 values
identify the inspected inputs, including the existing mod build. Full inventories,
stack traces, and harness artifacts remain in ignored `out/`. Refresh the summary
with the local `record` recipe when intentionally recording another run.

## Evidence and earliest blockers

The inspected installation identifies itself as **1.7.5000 rev1275**. Its
`RimWorld.runtimeconfig.json` selects **10.0.15-waylithclr.dev**, not stock Mono or
Unity Player. It enables custom assembly redirects, field injection, relaxed
member binding/access, and patchable JIT assemblies. Stock browser .NET does not
implement those custom switches merely by receiving the configuration file.
Whether each switch is necessary for a particular game path needs testing.
The runtime's `browser-jobs` checkpoint now reaches an unconditional dependency:
`PrepatcherCompat.Install` cannot load `Waylith.Injection.Injection` because its
fields reference `WaylithClr.FieldInjectionBatch` in `System.Runtime`. Stock
browser .NET lacks that type. See [runtime evidence](../../../slopweb/runtime/README.md).

Actual browser results below combine the primary's original game-entry-point
probe with this directory's final capability probe, summarized in
[`evidence/result.json`](evidence/result.json). Both run .NET 10.0.14 with
architecture `Wasm` and `browser: true`. The compatibility probe uses the debug
interpreter, without AOT or WASM threads, and with trimming disabled:

| Check | Observed result |
| --- | --- |
| Real `Verse.GenMath.PositiveMod(-7, 5)` | `3` |
| Real `Verse.IntVec3(3, 0, 4).LengthHorizontalSquared` | `25` |
| Real game version | `1.7.5000` |
| `UnityEngine.Mathf.Sqrt(81)` | `9` |
| Load `Waylith` managed assembly | Success; does not initialize the engine |
| Emit IL, create delegate, invoke `DynamicMethod` | Returns `42`; supported=true, compiled=false |
| Bundled Harmony 2.4.2 `Patch` | `PlatformNotSupportedException: OS kind Unknown not supported` |
| Load actual `SlopWorld.dll` via `AssemblyLoadContext.LoadFromStream` | Success, 2,355,712 bytes; no mod initialization |
| Construct `TcpClient` | `PlatformNotSupportedException: SystemNetSockets_PlatformNotSupported` |
| Construct `SocketsHttpHandler` | Success; construction alone is insufficient |
| Set the mod's `SocketsHttpHandler.UseProxy = false` | `PlatformNotSupportedException` |
| Actual `SocketsHttpHandler.SendAsync` without those options | `PlatformNotSupportedException` in `SendAsync` |
| Default browser-backed `HttpClient` reads same-origin `index.html` | HTTP 200 |
| Create anonymous 4096-byte memory map | Success; not evidence for file-backed mmap |
| Start managed thread | `PlatformNotSupportedException` |
| Invoke original game entry point | Logger's `Thread.Start` throws before engine construction |
| Initialize GLFW separately | `DllNotFoundException: glfw` in its static constructor |

The earliest **observed original-entry-point blocker** is
`RimWorldWaylith.Program.Main → InitLogger → Brutal.Logging.LogSystem.Initialize →
Microsoft.Extensions.Logging.Console.ConsoleLoggerProcessor..ctor → Thread.Start`.
`Program.Main` calls `InitLogger` at `IL_002f`. Replacing console logging or using a
working threaded browser runtime would move this checkpoint; it would not prove
that the game can reach a colony.

The next independently demonstrated native boundary is
`Brutal.GlfwApi.Glfw..cctor`, whose `IL_0052` calls
`NativeLibrary.Load("glfw", ...)`. In the normal graphical path,
`Waylith.EngineApplication..ctor` calls `Glfw.add_ErrorCallback` at `IL_0273`,
before its initialize callback can load the game. The constructor also sets up
Vulkan; its native graphics, window/input, and rendering APIs need browser
implementations. A `Headless` branch exists but initializes the configured audio
backend and runs a synchronous loop. It is not a browser rendering port.

The base game has **17 DynamicMethod construction call sites**: fifteen in
`Verse.DirectXmlToObjectNew`, one in
`RimWorld.Planet.PlanetLayer.GetOrCreateTileFactoryMethod`, and one in
`LudeonTK.NativeArrayUtility.ArrayAccessor<T>` initialization. DynamicMethod
construction alone cannot determine whether these paths work. The browser's
successful emitted-IL invocation is stronger evidence, but still does not exercise
every XML parser or the customized WaylithCLR semantics. In particular, working
dynamic IL does not imply working native method detours.

`RuntimeProbe.cs` adds emit/delegate/invoke, actual Harmony patch/restore, raw TCP,
`SocketsHttpHandler`, and thread-start checks. The host control on .NET 10.0.14
X64 returned `42` from generated IL and observed Harmony behavior `3 → 6 → 3`.
The browser failure is in
`HarmonyLib.PatchFunctions.UpdateWrapper → PatchProcessor.Patch → Harmony.Patch`.
This establishes a concrete SlopWorld patching blocker independently of the game's
earlier logger/graphics failures. No claim is made that all of Harmony is
impossible in browsers; the bundled implementation rejects this browser platform.
Use build-time IL transformation or explicit hooks unless a compatible patching
backend is implemented and tested. The mod's assembly loads successfully, which
establishes loadability only, not working constructors, dependency initialization,
or colony integration.

HTTP is an independently confirmed mod blocker: the existing transport's
`UseProxy = false` initializer throws. A separate probe leaves that option alone
and reaches `SocketsHttpHandler.SendAsync`, which also throws. The default
browser-backed `HttpClient` returns HTTP 200 for the same read-only harness page,
so this failure is specific to the sockets handler rather than page availability.

## Explicit offline hook

`just browser-offline` demonstrates one actual SlopWorld hook without native
detours. `../../../slopweb/offline/` uses pinned Mono.Cecil 0.11.6 to insert a call to the real
`SlopWorld.Patch_RunInBackground.Prefix(ref bool)` at the start of the installed
`Verse.Prefs.set_RunInBackground(bool)`. The setter's original body is retained;
only this prefix's visibility is widened in the copied mod to permit the call
across assemblies. Production assemblies remain unchanged.

The runtime invokes the real setter with `false, true, false`. A fresh baseline
page observes `False,True,False`; the separately staged patched page observes
`True,True,True`. It temporarily installs an isolated `PrefsData` object and restores
the original field afterward. It does not load a colony, save preferences, run
`PatchAll`, or initialize the mod. The original Harmony capability failure remains
independently reproducible.

The tool accepts only this explicit signature and refuses duplicate installation.
It records source and staged hashes in ignored `mod/offline.json`, updates the
game manifest, and stages modified DLLs only under `runtime/out/wwwroot-offline`.
The normal adapted root remains separate. Reports are
`../artifacts/offline-baseline.json` and `../artifacts/offline-patched.json`.
This is not a general Harmony translator: transpilers, patch ordering, dynamic
patch changes and custom WaylithCLR field injection remain unimplemented.

## Core-only startup adaptations

`../../../slopweb/offline/CoreBootstrap.cs` separately adapts startup for a Core-only browser stage.
It routes custom injection callbacks to explicit guards rejecting mod assemblies,
preserves the installed synchronous loading/write paths, and delegates the
blocking cross-reference helper to a serial implementation. It also records coarse
loader progress and disables desktop Steam initialization. This stage does not
apply the SlopWorld hook or implement arbitrary runtime patching; see the
[Entry checkpoint](../../../slopweb/runtime/README.md#core-only-entry-startup).

## Mod-specific adaptation owners

| Owner | Existing dependency | Browser adaptation needed |
| --- | --- | --- |
| `Bootstrap/ModEntry`, `ModProfile`, `Settings/ProfileSettings` | Profile TOML and `slopworld.profile` marker in game save folder | Mount an isolated writable profile before XML/mod startup; persist it explicitly |
| `Bootstrap/EarlyBootstrap`, `ModBootstrap`, `Patches/` | Early Harmony patching plus runtime transpilers and private-member access | Bundled Harmony fails in browser; transform IL before execution or provide a compatible backend/explicit hooks |
| `UI/Text/NativeFonts` | `Task.Run(Scan)`, blocking `GetResult`, host font enumeration | Supply known font assets, async initialization, and GUI font registration |
| `Platform/Windowing`, `Platform/Fonts`, idle inhibition | X11/XInput/Xss, Fontconfig, DBus | Browser input, canvas, bundled fonts, visibility/fullscreen APIs; omit desktop-only services |
| `Client/Daemon/DaemonHttpTransport` | Explicit `SocketsHttpHandler`, callback pumping | Browser-backed HTTP; retain deadline/admission and main-thread callback semantics |
| `Client/Transport/MiniWebSocket` | `TcpClient`, `SslStream`, blocking handshake, reader/writer threads | Browser WebSocket adapter; preserve protocol, bounded queues, ordering, reconnect generation |
| `Client/Daemon/Endpoint` | Host `endpoints/local.toml` and environment variables | Host supplies endpoint association; browser does not read host credentials |
| `Platform/Processes` and desktop clipboard | Child processes, desktop protocols | Host services or browser APIs; do not attempt process creation in WASM |

The inspected mod DLL has 32 P/Invoke declarations: X11 (16), Xss (3), Fontconfig
(10), Xi (3). Game dependencies include GLFW, Vulkan/VMA, Slang, ImGui, FreeType,
KTX, STB, audio, and Steam APIs. Metadata also includes Windows/macOS paths that
are not necessarily executed. Keep game asset data and shader conversion within
the graphics/asset owner's implementation.

Game loading itself starts threads in
`Verse.LongEventHandler.UpdateCurrentAsynchronousEvent` (`IL_0035`) and
`Verse.DirectXmlLoader.XmlAssetsInModFolder` (`IL_01be`). Waylith also starts a
render thread. A single-thread runtime needs scheduling changes beyond the logger.
Existing synchronous file APIs may work against a browser virtual filesystem;
they cannot see the host installation without explicit mounting or asset delivery.
The anonymous mmap probe alone does not establish file-backed support. The
separate runtime `browser-assets` checkpoint now transfers the installed pack
into the browser VFS and opens it with the real `Waylith.Assets.AssetPack` reader;
see [runtime evidence](../../../slopweb/runtime/README.md). Other mapped asset paths still need
their own coverage.

## Integration interfaces

**Managed runtime probe:** add this source file and reference the existing Harmony
assembly in the runtime worker's project (paths relative to `runtime/`):

```xml
<Compile Include="../compat/RuntimeProbe.cs" Link="RuntimeProbe.cs" />
<Reference Include="0Harmony">
  <HintPath>../../../mod/Assemblies/0Harmony.dll</HintPath>
</Reference>
```

Call `SlopWorld.BrowserCompat.RuntimeProbe.Run()` from a JS-exported method and
capture its JSON return value, or await `RunAsync(readOnlyUrl)` to also compare
sockets-backed and default browser-backed HTTP against a same-origin static page.
The standalone `Browser/` project already integrates both methods and loads the
actual mod DLL without running its constructor. Call after the runtime is ready. Keep each check's
exception chain. A successful thread `Start` only means startup was admitted;
the probe deliberately does not block the browser main thread with `Join`.
Record whether the runtime uses interpreter, AOT, and WASM threads alongside the
result. The probe never starts the game, changes its saves, or contacts the daemon.
The `browser-probe` recipe uses the primary-owned harness unchanged, including its
loopback server and Chromium runner; the browser build uses the installed SDK.

**Host `slopd` bridge:** keep daemon process, tmux, PTYs, sandboxes, projects, and
credentials on the host. Existing wire contract remains binary Protobuf:
`application/x-protobuf`, `/api/*`, `x-slop-token`, optional `x-slop-session`, and
`/ws` with subprotocol `slopworld.protobuf.v2`. See
[`shared/protocol.yaml`](../../../shared/protocol.yaml) and
[`notes/protocol-wire.md`](../../../notes/protocol-wire.md).

The browser WebSocket constructor accepts URL and protocols, not arbitrary
authentication headers ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/WebSocket/WebSocket)).
`slopd/src/api/mod.rs::presented_token` reads `x-slop-token`, including for the WS
upgrade. Direct substitution of a browser WebSocket therefore cannot authenticate
the current daemon contract. HTTP's explicit `SocketsHttpHandler` must also change;
browser HTTP is provided through the browser network stack
([Microsoft](https://learn.microsoft.com/en-us/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0)).

The current harness implements only same-origin `GET /host/health`, forwarding
to `/api/health` with host-held credentials. A colony implementation would extend
this host boundary with explicit authorized HTTP routes and a WebSocket relay.
That relay must inject the daemon token on the host handshake, negotiate
`slopworld.protobuf.v2`, preserve binary payloads and ordering, and preserve the
32 MiB frame/message bound. Bind to loopback and validate browser origin/session
before forwarding authority. Never put a daemon root token in a URL or a static
asset. Use the same origin for browser HTTP and WS; a remote deployment needs an
authenticated TLS host gateway. This is the required interface, not an assertion
that the current health-only harness supports colony control.

Keep `MainThreadCompletions` and `Root.Update` pumping semantics. The socket
interface's synchronous `Connect` and `FlushOutgoing` need async browser lifecycle
integration: page shutdown cannot safely block while waiting for an acknowledgment.
Do not weaken protocol generations, bounded admission, or session/run identity
checks when replacing the transport.
