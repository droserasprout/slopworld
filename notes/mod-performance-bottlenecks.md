# C# mod performance bottlenecks

Static review of the committed C# mod at `a3c4c28` (`Fix F12 terminal opening from settings`). This is a list of likely hot paths, not a runtime profile; confirm priority with the Unity Profiler.

## Highest priority

- **Synchronous reconnects can freeze the game.** `Root.Update` pumps `SessionHub` every frame. When the socket is down, `HubTransport.Update` calls `MiniWebSocket.Connect` on Unity's main thread. TCP connection and WebSocket handshake timeouts therefore block the menu or game frame.
  - [ModEntry.cs:447](../mod/Source/SlopWorld/ModEntry.cs#L447)
  - [HubTransport.cs:70](../mod/Source/SlopWorld/Client/SessionHub/HubTransport.cs#L70)
  - [MiniWebSocket.cs:35](../mod/Source/SlopWorld/Client/MiniWebSocket.cs#L35)

- **Inbound work is drained without a per-frame budget.** WebSocket events are parsed and dispatched in a `while` loop on the main thread. HTTP completions are drained the same way. A burst of screen events or responses can consume an entire frame and amplify the cost of the UI callbacks.
  - [SessionHub.cs:76](../mod/Source/SlopWorld/Client/SessionHub.cs#L76)
  - [HubTransport.cs:92](../mod/Source/SlopWorld/Client/SessionHub/HubTransport.cs#L92)
  - [DaemonClient.cs:111](../mod/Source/SlopWorld/Client/DaemonClient.cs#L111)

- **Agent sidebar layout scales poorly with projects, agents, and workers.** Colonist-bar integration rebuilds layout data during GUI passes. `AgentCounts` scans all sessions once per project; `WorkerCount` scans all workers once per project; buckets are sorted every placement. Status filtering reparses the settings string for each check, and row drawing repeatedly measures and rebuilds title/status strings.
  - [AgentSidebar.Layout.cs:48](../mod/Source/SlopWorld/Patches/AgentSidebar/AgentSidebar.Layout.cs#L48)
  - [AgentSidebar.Layout.cs:149](../mod/Source/SlopWorld/Patches/AgentSidebar/AgentSidebar.Layout.cs#L149)
  - [AgentSidebar.RowGeometry.cs:12](../mod/Source/SlopWorld/Patches/AgentSidebar/AgentSidebar.RowGeometry.cs#L12)
  - [AgentSidebar.Views.cs:154](../mod/Source/SlopWorld/Patches/AgentSidebar/AgentSidebar.Views.cs#L154)
  - [SidebarRowRenderer.cs:72](../mod/Source/SlopWorld/Patches/AgentSidebar/SidebarRowRenderer.cs#L72)

- **Terminal frames are reparsed on every screen update.** `ScreenBuf.FromJson` replaces the line array and invalidates runs. The next draw parses every row and performs URL scanning over the complete rows-by-columns buffer. The render-texture fallback disables caching permanently, after which each repaint performs a full terminal paint.
  - [ScreenBuf.Json.cs:7](../mod/Source/SlopWorld/Client/SessionHub/ScreenBuf.Json.cs#L7)
  - [TerminalWindow.Selection.cs:566](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Selection.cs#L566)
  - [Sgr.cs:65](../mod/Source/SlopWorld/UI/Sgr.cs#L65)
  - [Sgr.cs:322](../mod/Source/SlopWorld/UI/Sgr.cs#L322)
  - [TerminalWindow.Rendering.cs:214](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Rendering.cs#L214)

## Simulation and input hot paths

- **Worksite assignment can create simulation spikes.** Every 15 ticks, each working agent may scan all building frames and call `CanReserve`, `CanConstruct`, footprint validation, blueprint placement checks, and pathfinding. The once-per-second sweep checks every frame for blockers and viable builders.
  - [Worksite.cs:20](../mod/Source/SlopWorld/Sim/Worksite.cs#L20)
  - [WorksiteErrands.cs:243](../mod/Source/SlopWorld/Sim/WorksiteErrands.cs#L243)
  - [Worksite.cs:291](../mod/Source/SlopWorld/Sim/Worksite.cs#L291)
  - [WorksiteErrands.cs:271](../mod/Source/SlopWorld/Sim/WorksiteErrands.cs#L271)

- **Expanded content trees still traverse all expanded nodes.** Visible rows are skipped, but measuring recursively counts every expanded descendant and drawing recursively walks every expanded branch. `FilesView.Groups()` also creates group objects/lists on each draw.
  - [ContentTreeView.cs:112](../mod/Source/SlopWorld/UI/ContentTreeView.cs#L112)
  - [ContentTreeView.cs:167](../mod/Source/SlopWorld/UI/ContentTreeView.cs#L167)
  - [ContentTreeView.cs:236](../mod/Source/SlopWorld/UI/ContentTreeView.cs#L236)
  - [FilesView.cs:59](../mod/Source/SlopWorld/UI/FilesView.cs#L59)

- **WebSocket sends allocate per input packet.** Each send creates UTF-8 bytes, a `MemoryStream`, a mask, a copied frame array, and performs byte-by-byte masking before writing and flushing. Typing, mouse reporting, and paste bursts will expose this most clearly.
  - [MiniWebSocket.cs:160](../mod/Source/SlopWorld/Client/MiniWebSocket.cs#L160)

## Triage order

Profile reconnect failure first, then `HubTransport.Update`, sidebar placement/row drawing, terminal `Sgr.ParseLines`, and `Worksite.Send`/`Free`/`Fits`. These paths cover the largest potential frame hitches and scale with external event rate, visible agents, terminal size, or map construction state.
