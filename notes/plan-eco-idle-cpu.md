# Eco idle CPU plan

Reduce foreground game CPU while Eco mode is resting, with frame pacing and visible
responsiveness unchanged. The daemon, socket, and agents must continue running.

## Goals and guardrails

- Reduce main-thread work and managed allocations when the visible pixels and data are unchanged.
- Keep foreground FPS, input, hover, selection, cursor, tooltips, and socket pumping live.
- Keep the Eco backdrop's drift smooth unless a separate visual setting explicitly changes.
- Preserve immediate redraw on Eco exit, terminal open/close, resize, settings changes, and session
  revisions.
- Do not lower `Application.targetFrameRate`, add sleeps, or stop `SessionHub`/the daemon.

## Work order

### 1. Instrument the actual idle frame

Add disabled-by-default `PerfTrace` lanes around the complete `Root.Update`,
`ColonistBarOnGUI`, sidebar rendering, `TopBar.Draw`, and `TerminalWindow.DoWindowContents`.

When live profiling is authorized, compare Eco on/off, terminal open/closed, 1/8/32 agents,
silent agents versus active output, and the same resolution and display settings. Record main-thread
time, managed allocations/GC, FPS, and input latency separately.

### 2. Cache sidebar and colonist-bar presentation

Start with the low-risk presentation layer:

- Sample wall time once per sidebar frame or second bucket, rather than once per row.
- Cache `Ago` text, state-name tooltips, agent indicators, header counts, and derived widths until
  state, session, settings, font, or the one-second age bucket changes.
- Reuse a session snapshot during one draw instead of repeating lookups for each row.

Then evaluate a render-texture cache for static sidebar/portrait output. Blit cached pixels during
`Repaint`, while retaining lightweight layout, hit testing, hover, and vanilla layout restoration
on the events that need them. Do not cache portraits until portrait/state invalidation is defined.
Invalidate on session/layout revisions, width or scroll changes, selection, settings, font/atlas
changes, and relevant pawn or state changes.

### 3. Cache top-bar chrome

`TopBarMapComponent` and the terminal top bar invoke the chrome for every GUI event. Separate its
static panel, doors, labels, and geometry from dynamic clock, quota, status, hover, and click work.
Refresh dynamic text at its natural one-second or data-revision cadence; keep hit testing and
tooltip registration responsive. A repaint cache is acceptable if it does not consume input or
hide hover feedback.

### 4. Remove unconditional no-op work

- Make `TaskStore.Update` wait on a realtime deadline without querying wall time every rendered
  frame; poll only while its Tasks view or any visible badge needs the data.
- Consider an Eco early exit for `RealClock` after preserving wall-clock correctness on Eco enter,
  leave, load, and map changes.
- Consider relying on the tick-boundary pause enforcement instead of `TimeKeeper`'s repeated
  per-frame assignment, but first test speed changes, external pauses, loads, and resume edges.
- Leave transport pumping intact; optimize it only if profiling finds non-empty or reconnect work
  in the idle case.

### 5. Reduce backdrop preparation

Cache the resident frame, material, screen-to-map fit, and camera-dependent geometry in `Eco`.
Recompute them on resolution, camera/UI geometry, texture, or dim-setting changes. Keep only the
animated translation and draw submission per frame so the existing drift remains smooth.

### 6. Revisit hidden map maintenance

Evaluate moving hidden fleck aging to the same cadence as mesh and sky maintenance. Account for
expiry accuracy and reveal behavior before accepting the saving.

### 7. Optimize active-output traffic separately

For active terminal output, implement the related priorities in [CPU and idle-state](plan-cpu-fixes.md): retain
ANSI base rows around URL decoration and coalesce stale live frames before full JSON decoding.
Daemon host-metadata batching and clean-screen wakeups are separate system-CPU work.

## Validation

For C# changes, run the applicable `make` checks, including `make BUILD=release test-mod`,
`make BUILD=release bench-mod`, and `make lint-mod`. Pure .NET benchmarks do not prove Unity GUI
CPU or input latency.

Live validation requires an explicit game run. With `SLOPWORLD_DEBUG=1`, confirm lower Eco main-
thread time and allocations without a foreground FPS reduction, delayed input, stale clock/status,
broken terminal updates, or a reveal hitch. Completion requires the idle UI caches to invalidate
correctly across Eco transitions, resize, settings, session revisions, scrolling, and active
terminal output.
