# CPU optimizations

Implemented hot-path reductions in the C# mod:

- `RealClock` samples wall time once per 60 frames.
- `Plague` walks the lister's plant list directly, shares one cached game tick
  across each batch, and reduces background work.
- `Radio` keeps vanilla music disabled each frame, checks steady-state audio every 10 frames,
  and reconnects pending selections immediately.
- `Aura` compares squared distances.
- `Sgr.Autolink` rejects rows without `://` before building a full-row string.
- `MarkdownPreview` caches selection geometry between reflows and skips off-screen placements,
  text lines and table rows before issuing IMGUI draw calls.

These came from tracing `Root.Update`, component update/tick, and `OnGUI` paths.
Treat the list as implementation history, not a current profile: measure before
doing more. The remaining broad candidates are sidebar layout rebuilding, terminal
parsing allocations, and `Endpoint.Resolve`, which reads and parses `endpoint.toml`
on every `Settings.Connection` and so runs from `SlopWidgets.Status` per event.

Load time rather than frame time is [startup-time](startup-time.md).
