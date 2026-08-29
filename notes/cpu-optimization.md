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
The list records implemented reductions; it is not a benchmark or a current profile.
