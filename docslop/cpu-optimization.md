# CPU optimizations

Implemented hot-path reductions in the C# mod:

- `RealClock` samples wall time once per 60 frames.
- `Plague` walks the lister's plant list directly, shares one cached game tick
  across each batch, and reduces background work.
- `Radio` checks state every 10 frames and stops vanilla music once.
- `Aura` compares squared distances.
- `Sgr.Autolink` rejects rows without `://` before building a full-row string.

These came from tracing `Root.Update`, component update/tick, and `OnGUI` paths.
Treat the list as implementation history, not a current profile: measure before
doing more. The remaining broad candidates are sidebar layout rebuilding and
terminal parsing allocations.
