# Terminal scrolling performance plan

Implementation and verification plan for the committed terminal path at `a3c4c28` (`Fix F12 terminal opening from settings`). The goal is smooth fractional scrolling while live output continues, without increasing history-request traffic or changing terminal behavior.

## 1. Measure before optimizing

Add temporary, opt-in debug instrumentation first. Do not emit one log line per frame or include terminal payloads. Accumulate counters and timings, then print one summary when a scroll gesture ends or once per second while it is active.

Capture these timestamps and counters:

- first accepted wheel/touchpad movement;
- history request queued, sent, reply received, and first frame whose requested anchor is displayable;
- `TerminalHistory.UpdateLive`, `TerminalHistory.TryView`, `Sgr.ParseLines`, URL scanning, `Paint`, and `Blit` duration;
- current anchor, target offset, requested offset, `ScreenBuf.Seq`, rows/columns, `LiveShift`, history-cache row count, pending request count, and whether `_noCache` is active;
- number of repaint frames, full paints, cache blits, history replies, stale replies, and live frames received during the gesture.

Use the Unity Profiler for GC allocation and frame time. If the Unity/runtime version supports it, also sample allocated bytes around the measured sections. Keep instrumentation behind a debug flag and avoid allocations in the measurement path itself.

Run the baseline matrix before changing behavior:

1. idle live pane;
2. live pane with continuous output;
3. first cold scroll into history;
4. fractional touchpad scrolling over already cached history;
5. integer PageUp/PageDown scrolling;
6. rapid reversal and deep scrolling;
7. panes containing ANSI color, URLs, emoji/wide characters, and selection/hover overlays;
8. resize, font/theme change, daemon latency, and render-texture-cache failure.

The first report should answer whether delay is input-to-request, request-to-reply, history assembly, parsing, painting, or GC—not just report total frame time.

## 2. Fix fractional-scroll rendering first

If the baseline confirms repeated full paints, keep the terminal's base content in a render texture for the current displayed history view and move/crop that texture for fractional offsets. Repaint the texture only when rows, terminal dimensions, theme/font revisions, or the displayed anchor change.

The current fractional branch calls `Paint` during every repaint, while the integer branch can use `Blit`:

- [TerminalWindow.Rendering.cs:13](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Rendering.cs#L13)
- [TerminalWindow.Rendering.cs:214](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Rendering.cs#L214)

Keep cursor, selection, hover, and history-bar overlays immediate-mode. They should not invalidate the cached base texture. Verify that the texture has enough overscan rows to prevent gaps at both edges during a fractional translation.

Acceptance criteria:

- offset-only frames perform a texture blit, not `Paint`;
- `Paint` count falls to content-change events plus one initial warm-up;
- fractional scrolling remains visually continuous at the target display rate;
- selection, cursor, links, and hit-testing remain aligned with the translated content.

## 3. Stop live redraws from invalidating visible history

While scrolled, `NoteLiveFrame` calls `TerminalHistory.UpdateLive` for each new live sequence. `UpdateLive` reindexes live rows and calls `Changed`, which invalidates the assembled view even when the rows currently being viewed have not changed:

- [TerminalWindow.Selection.cs:13](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Selection.cs#L13)
- [TerminalHistory.cs:41](../mod/Source/SlopWorld/UI/TerminalHistory.cs#L41)
- [TerminalHistory.cs:89](../mod/Source/SlopWorld/UI/TerminalHistory.cs#L89)

Separate live-coordinate maintenance from displayed-history invalidation. Preserve the current `TryView` result and parsed rows when a live redraw does not affect the visible history anchor. If a real terminal scroll changes coordinates, update the coordinate transform and only invalidate rows that can enter the displayed view.

Acceptance criteria while the agent produces continuous output:

- no new `ScreenBuf`/line-array assembly for an unchanged visible history anchor;
- no full SGR parse caused solely by an unrelated live redraw;
- live shifts keep the selected content anchored;
- stale replies still populate valid history without replacing a newer visible view.

## 4. Remove history-cache copy churn

When live output shifts, `TerminalHistory.Shift` creates a second dictionary and copies the cached rows:

- [TerminalHistory.cs:205](../mod/Source/SlopWorld/UI/TerminalHistory.cs#L205)

First verify how often this occurs and how large `_lines` is. If it is material, replace physical key movement with a logical base offset or ring-buffer representation. Keep the existing bounded `-10,000..199` range and preserve overlap semantics.

Acceptance criteria:

- no full dictionary copy per live scroll;
- bounded memory remains unchanged;
- history rows, coordinate shifts, and stale-reply translation remain correct across repeated live scrolling.

## 5. Reuse parsed rows and avoid unconditional URL scans

`TryView` currently creates a new line array and `ScreenBuf`; `EnsureRuns` then parses the view. `Sgr.Autolink` builds a complete rows-by-columns character buffer even when no URL is present:

- [TerminalWindow.Selection.cs:566](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.Selection.cs#L566)
- [Sgr.cs:65](../mod/Source/SlopWorld/UI/Sgr.cs#L65)
- [Sgr.cs:322](../mod/Source/SlopWorld/UI/Sgr.cs#L322)

After cache invalidation is corrected, add row-level reuse keyed by row content, terminal width, and theme revision. Add a cheap plain-text/no-link fast path before full URL scanning; retain the full parser for rows containing escape/link candidates.

Acceptance criteria:

- moving across already indexed rows does not reparse unchanged rows;
- plain terminal output avoids the full URL-grid allocation;
- ANSI, OSC 8 links, wrapped URLs, wide characters, and theme changes remain correct.

## 6. Revisit request scheduling only if measurement requires it

The current client already coalesces targets, uses overlapping prefetch windows, limits requests in flight, and sends at a 60 Hz beat:

- [TerminalWindow.State.cs:656](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.State.cs#L656)
- [TerminalWindow.State.cs:802](../mod/Source/SlopWorld/UI/TerminalWindow/TerminalWindow.State.cs#L802)

Do not replace this with one request per wheel event. If request-to-visible latency dominates after local rendering is fixed, measure daemon capture/serialization latency separately and consider the existing [history warm-up plan](mod-terminal-history-warmup.md). If replies backlog, coalesce obsolete targets without dropping request metadata needed for stale-reply translation.

## 7. Verification and regression checks

For every implementation phase, compare against the baseline using the same terminal size, output workload, and scroll gesture. Record:

- median and worst frame time during fractional scrolling;
- input-to-visible and request-to-visible latency;
- GC allocation and collection activity;
- full-paint count, parse count, cache-hit ratio, and history-request rate;
- memory retained by history and parsed-row caches.

Correctness checks must cover cold first scroll, continuous live output while scrolled, rapid direction changes, reaching the real top, returning to live, resize/theme/font changes, stale or delayed replies, selection movement, URL hover, and cache fallback. Remove or disable temporary debug instrumentation after the measurements are captured, then record the measured result and any remaining bottleneck here.
