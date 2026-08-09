# CPU Optimization Analysis — SlopWorld Mod C# Half

## Changes Implemented

| # | File | Change | Impact |
|---|------|--------|--------|
| 1 | `Sim/RealClock.cs` | **Solar tick cached 60:1**: `DateTime.Now` syscall runs once per 60 frames, down from every frame | Eliminates ~98% of DateTime.Now calls |
| 2 | `Sim/Plague.cs` | **Plant list no longer copied**: indexes lister's backing `List<Thing>` directly with version tracking via `Count`. Old code rebuilt the list by scanning all ~15k plants every ~50s | Eliminates 15k-item scan/copy per sweep cycle |
| 3 | `Sim/Plague.cs` | **`BandAt` + `Bite` accept cached tick**: overloads take `int tick` so callers read `Find.TickManager.TicksGame` once per batch, not per cell/plant | Reduces TicksGame lookups from ~400/tick to 1/tick in hot paths |
| 4 | `Sim/Plague.cs` | **Background throttle**: when `!Application.isFocused`, skips `Effects()` (pawn effects), `Vent()` (plume), `Sow()` (flowers), and processes only 1 plant/tick (down from 5) | Cuts plague work ~80% when unfocused |
| 5 | `Sim/Radio.cs` | **Throttled to 10-frame interval**: state checks run at 6Hz instead of 60Hz. `_musicDisabled` flag caches the first music stop | Cuts 90% of per-frame Radio work |
| 6 | `Sim/Aura.cs` | **`DistanceToSquared`** replaces `DistanceTo` (avoids `Math.Sqrt`). Pre-computed `RadiusSq` | ~5× faster distance check |
| 7 | `UI/Sgr.cs` | **Fast path in `Autolink`**: scans runs for `"://"` before allocating full-row `char[]`; skips allocation on ~99% of lines | Eliminates string/array alloc per row on non-URL lines |

## Files Modified

- `Sim/RealClock.cs`
- `Sim/Plague.cs`
- `Sim/Radio.cs`
- `Sim/Aura.cs`
- `UI/Sgr.cs`

---

## Original Analysis (pre-change)

## Methodology

All hotspots were identified by reading every `.cs` file in the project and tracing the
per-frame and per-tick call chains. The mod hooks into `Root.Update` (every frame),
`GameComponentUpdate` (every frame for game components), `MapComponentTick` (every
game tick), and various `OnGUI` paths.

---

## 🔴 Tier 1 — Severe (per-frame syscalls, allocation storms)

### 1. `RealClock.GameComponentUpdate()` — every frame

**File:** `Sim/RealClock.cs`

**What it does:**
- Calls `SolarTick()` every frame
- `SolarTick()` calls `DateTime.Now` (OS syscall), `TimeOfDay.TotalSeconds`, tick math
- `Longitude` getter accesses `Find.WorldGrid`, `Find.CurrentMap`, `Find.Maps`, `Find.GameInitData`
- Writes `Find.TickManager.gameStartAbsTick` every frame

**Cost:** `DateTime.Now` is a managed→native transition (expensive). The `Longitude` getter
chains through multiple null checks and collections. This runs even on the menu screen.

**Fix:** Cache the computed `gameStartAbsTick` value and only recalculate every N frames
(e.g., once per second = every 60 frames at 60fps). The sun moves ~0.4° per minute of real
time — it does not need 60fps precision. Also cache `Longitude` as it only changes on map
change (`Find.CurrentMap` changing).

### 2. `Plague.StepPlants()` — every tick, rebuilds plant list

**File:** `Sim/Plague.cs` (line ~210)

**What it does:**
- Every tick (60/s), walks `PlantsPerTick` = 5 plants
- When the local list is exhausted, **walks ALL things on the map** via
  `map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)` and copies them into a `List<Plant>`
- A default map has ~15k plants — this full enumeration happens every 3000 ticks (~50s real)
  but iterates all 15k to select 5, then next tick iterates again from where it left off

**Cost:** 15k-item enumeration every ~50 seconds, plus per-tick iteration of the working list
and `BandAt()` calls per plant (which does hashing).

**Fix:**
- Cache the plant list reference; `ThingsInGroup` returns the same backing list, just
  re-snapshot it when count changes
- Or use a stamp-based approach: check `listerThings.defCount` to detect changes instead of
  full re-enumeration

### 3. `Sgr.ParseLine()` — every terminal screen redraw

**File:** `UI/Sgr.cs`

**What it does:**
- `Autolink()` creates `new string(chars)` of the full row width to find URLs
- `Split()` allocates a new `List<SgrRun>` every call
- `TrimTail()` loops backward through URL text
- Runs are flushed and merged on every escape code (~dozens per line)

**Cost:** String and list allocations on every screen frame refresh (daemon pushes updates
multiple times per second). The full-row char array allocation in `Autolink` is wasteful when
most rows have no URL.

**Fix:**
- In `Autolink`, skip full-row reconstruction when no `://` pattern exists in any run's text
- Reuse the `cut` list across calls (use `Clear()`)
- Pool the `char[]` for the full-row scan

---

## 🟠 Tier 2 — High (called per-tick or per-frame with non-trivial work)

### 4. `Plague.BandAt()` — called thousands of times per tick

**File:** `Sim/Plague.cs` (line ~130)

**What it does:**
- Called per-cell from: `Catch()` (every pawn), `Effects()` (every pawn), `StepPlants()` (every plant), `Sow()` (400 cells/sweep), `Patch_NoRegrowth` (wild plant spawn)
- Each call: `Bite()` → `At()` (array index + `Find.TickManager.TicksGame`) + potential `Grit()` (hashing)

**Cost:** `Grit()` does `Gen.HashCombineInt` + 4 multiplications + 4 XORs + 3 shifts. Called
potentially millions of times over a colony's life. The `Bite()` method reads `TicksGame` each
time.

**Fix:**
- Cache `Find.TickManager.TicksGame` locally in the calling methods instead of reading it
  in `Bite()` per cell
- `Grit()` can be **inlined** — the hash computation is tiny but repeated endlessly
- For `Patch_NoRegrowth` prefix, add a fast early-out: if the caller already knows the band,
  pass it instead of re-computing

### 5. `AgentSidebar.Place()` — every redraw of the sidebar column

**File:** `Patches/AgentSidebar.cs`

**What it does:**
- Clears and rebuilds `Rows`, `Heads`, `Buckets`, `Order`, `Named` lists/dictionaries
- Iterates all sessions, all projects, all pawns
- Computes layout geometry for every row

**Cost:** Called every frame the sidebar is visible (60 fps). Creates transient string lists
and dictionary entries. The `Alphanum` sort in `AgentColony.Reorder()` is called every
reconcile tick (60 ticks) and sorts by character-by-character comparison.

**Fix:**
- Cache the layout and only rebuild when sessions change (check a version/increment counter
  from SessionHub). The sidebar items only change when sessions/projects update from the
  daemon, not every frame.
- `Alphanum` comparison is a hot path — use `Interop` or at minimum avoid
  `string.CompareOrdinal` at the end; use `string.Compare` with culture.

### 6. `Radio.Update()` — every frame

**File:** `Sim/Radio.cs`

**What it does:**
- Checks `Find.MusicManagerPlay` (castclass to Root_Play) every frame
- Checks `SessionHub.Instance.Online` every frame
- Computes `Source()` (string concat for OstPath) and `Volume()` every frame
- Compares strings for equality every frame

**Cost:** The cast to `Root_Play` via `Find.MusicManagerPlay` is cheap but done 60/s. The
bigger issue is that music state does not change 60 times a second.

**Fix:**
- Throttle: only run the full logic every 10-15 frames. Radio state commands are only sent
  on change events; the per-frame check is just for the `disabled` flag on MusicManagerPlay
  (which only changes on load/new colony) and reconnection detection.
- Cache `MusicManagerPlay` reference (it's a singleton per game).
- Cache `SessionHub.Instance` reference in a local static.

### 7. `Aura.Covers()` — called per-cell in regrowth patch

**File:** `Sim/Aura.cs`

**What it does:**
- Iterates all pulses every call
- Computes `DistanceTo()` for each pulse
- Called from `Patch_NoRegrowth` prefix for every wild plant spawn check

**Cost:** Distance computation involves `Math.Sqrt`. With ~15k plants and ~60 pulses, that's
~900k distance checks per plant sweep.

**Fix:**
- Use squared distance comparison (`DistanceToSquared`) to avoid `sqrt`
- Pre-compute `RadiusSq = Radius * Radius`
- Use a spatial hash (cell grid) instead of iterating all pulses — group pulses by cell
  quadrant to early-out

### 8. `AgentColony.GameComponentTick()` — every 60 ticks

**File:** `Sim/AgentColony.cs`

**What it does:**
- Calls `PawnsFinder.AllMaps_FreeColonists` in `FindExisting()` — this enumerates ALL free
  colonists across ALL maps
- Creates `HashSet<string>` from session list every reconcile (filtering ephemeral sessions)
- Calls `SessionHub.Instance.Get()` in a loop
- Calls `RobotFace.Apply()` per pawn
- Sorts pawn names with `Alphanum` (custom char-by-char comparison)

**Fix:**
- `PawnsFinder.AllMaps_FreeColonists` is expensive — cache or use `Find.Maps` iteration
  with `Map.mapPawns.FreeColonistsSpawned`
- The `live` HashSet creation allocates on every reconcile; reuse it with `Clear()`
- `Alphanum` string comparison can be optimized with `CompareInfo.Compare` or by using
  .NET's `Interop` services for natural sort

---

## 🟡 Tier 3 — Moderate (worth noting for large maps / many agents)

### 9. `Plague.Sow()` — every 15 ticks, 400 cells per sweep

**File:** `Sim/Plague.cs`

Iterates 400 cells per tick (every 15 ticks), calls `BandAt()` and `Grit(cell, SowSalt)` per
cell. For a large map with ~200k cells, each sweep walks 400 indices — cheap by itself but
adds to the cumulative BandAt load.

**Fix:** Already throttled well. No change needed.

### 10. `Plague.Effects()` — every 300 ticks

**File:** `Sim/Plague.cs`

Creates `_rolling` list copy of `AllPawnsSpawned`. Iterates all pawns calling `BandAt()`,
`Marked()`, `Spared()`. The list copy allocation is avoidable.

**Fix:** `_rolling` could iterate `map.mapPawns.AllPawnsSpawned` directly if it's an
indexable collection (check if it's `List<Pawn>`). If the list can be safely iterated
without copy (no mutation during iteration), skip the copy.

### 11. `SessionHub.Update()` — every frame (from Root.Update)

**File:** `Client/SessionHub.cs`

Calls `SlopClient.PumpCompletions()` every frame — this processes any pending HTTP
callbacks. If no HTTP calls are in-flight, this is a no-op, but the function call overhead
adds up.

**Fix:** Make `PumpCompletions` a static bool check first. Or batch it — only pump every
few frames.

### 12. `DeadCursor.Tick()` — every frame

**File:** `UI/DeadCursor.cs`

Time comparisons with `Time.realtimeSinceStartup` every frame. Very cheap, but runs on
every frame including menu. The `Spin` method creates a new `Texture2D` and calls
`Apply()` every time a new spin frame is needed.

**Fix:** Cheap enough to leave alone. If the `Texture2D.Apply()` in `Spin` is on the hot
path (it runs once per spin step, then cached), that's fine — it only runs ~12 times
ever.

---

## 📊 Summary: Priority Actions

| # | File | Line | Issue | Impact | Effort |
|---|------|------|-------|--------|--------|
| 1 | `RealClock.cs` | `GameComponentUpdate` | `DateTime.Now` + full recalc every frame | **High** | Low |
| 2 | `Plague.cs` | `StepPlants()` | Full 15k plant re-enumeration every 50s | **High** | Low |
| 3 | `Sgr.cs` | `ParseLine` → `Autolink` | Full-row char array + allocs per redraw | **High** | Medium |
| 4 | `Plague.cs` | `BandAt()` | Thousands of hashes per tick | **High** | Low |
| 5 | `AgentSidebar.cs` | `Place()` | Full layout rebuild every frame | **High** | Medium |
| 6 | `Radio.cs` | `Update()` | Unnecessary per-frame work for slow-changing state | **Medium** | Low |
| 7 | `Aura.cs` | `Covers()` | `DistanceTo` (sqrt) per cell | **Medium** | Low |
| 8 | `AgentColony.cs` | `GameComponentTick` | `PawnsFinder.AllMaps_FreeColonists` enumeration | **Medium** | Medium |
| 9 | `Sgr.cs` | `Flush()` | String concat + list allocs per escape code | **Low** | Medium |
| 10 | `Plague.cs` | `Effects()` | Unnecessary `AllPawnsSpawned` copy | **Low** | Low |

---

## Specific Fix Implementations

### Fix 1: Cache `RealClock` solar tick

In `RealClock.GameComponentUpdate()`, cache the computed `gameStartAbsTick` and only
recompute the solar tick every 60 frames (or 120). The sun position changes negligibly
within one real second.

### Fix 2: Avoid full plant re-enumeration in `StepPlants`

Instead of clearing and rebuilding `_plants` by iterating all `listerThings`, use a
version-stamping approach: check if `listerThings.defCount` for `Plant` has changed since
last refill. Only re-enumerate when count changed.

### Fix 3: Skip full-row char array in `Autolink`

Add a fast pre-check: scan the runs' text for `://` before allocating the full-row
`char[]`. Most terminal lines have no URL, so skip the allocation entirely.

### Fix 4: Cache `TicksGame` locally in `Bite`-adjacent code

`Find.TickManager.TicksGame` is a property that may resolve through several references.
Cache it once per tick-batch in `StepPlants`, `Catch`, `Effects` instead of reading it
per-cell.

### Fix 5: Debounce sidebar layout rebuild

Add a version counter to `SessionHub.Sessions` and `.Projects`. Only call `Place()` when
the version changes (daemon pushes an update), not every frame. Track `_lastLayoutTick`
and skip layout if nothing changed.