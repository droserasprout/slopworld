# CPU hot-path reductions

The C# mod reduces work in these hot paths:

- `RealClock` samples wall time once per elapsed second, independent of FPS.
- `Plague` walks the lister's plant list directly, shares one cached game tick
  across each batch, and reduces background work.
- `Radio` keeps vanilla music disabled each frame, checks steady-state audio on a 1/6-second
  deadline, and reconnects pending selections immediately. Muted sidecar music is stopped
  only on a state change or if playback resumes.
- `Aura` compares squared distances.
- `Sgr.Autolink` rejects rows without `://` before building a full-row string.
- `MarkdownPreview` caches selection geometry between reflows and skips off-screen placements,
  text lines and table rows before issuing IMGUI draw calls.
- `HubEventBatch` reuses bounded message and coalescing buffers. Empty socket frames allocate
  nothing in the batch reader; parsed payloads are released after dispatch. History replies
  and control events retain their order while live screens coalesce per session.
- Sidebar layout already uses revision and geometry keys. Title cleanup now caches by session
  label/title/path/host state and font identity/atlas revision; removed sessions are weakly held.
  Off-screen agent labels, hover paint and project headings skip their repaint work.
- Terminal cursor and selection painting run only on Repaint; input and hover tracking remain
  live. Text already uses a render-texture cache and changed-row repaint policy.
- Full terminal coverage suppresses weather and map-edge drawing as well as the existing map
  mesh, dynamic things, flecks and overlays. Mesh maintenance continues to avoid a reveal hitch.

`make BUILD=release bench-mod` includes idle socket allocation and unchanged-title comparisons
against the previous allocation/cleanup patterns, plus the idle terminal repaint decision.
These measure helpers under .NET, not Unity CPU load or input latency. Opt-in `PerfTrace`
adds sidebar layout hits/rebuilds, title rebuilds, and terminal cache hits for runtime checking.
