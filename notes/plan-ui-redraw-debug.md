# UI redraw debug overlay
Status: proposed

## Problem and scope

Make unexpected terminal and sidebar render work visible while investigating Mono
allocation and frame-time costs. Start with terminal cache updates, then add sidebar
layout and render updates where their owners can report meaningful events.
An idle view should become quiet after the last flash expires.

Use the Broadway convention supplied in the discussion as the reference:
Green means the renderer reused an existing node and texture. Magenta means the
renderer received a node again and used a cached texture. This includes solid-color
nodes. Red means the renderer uploaded new texture pixels. The supplied reference is
`gtk/gdk/broadway/broadway.js:1593`, with 100 ms flashes and at most 200 overlays
per update. This is a design reference, not a verified dependency of the mod.

Follow [terminal rendering](mod-terminal-rendering.md),
[panel ownership](mod-terminal.md), [sidebar ownership](mod-sidebar.md), and
[workspace geometry](ui-dynamic-layout-architecture.md).

## Event semantics

Adapt the colors to local Unity rendering, with a visible legend:

| Color | SlopWorld meaning |
| --- | --- |
| Green | An actual placement or composition update reuses cached render content. |
| Magenta | CPU-side render content or layout is rebuilt using existing textures or glyphs. |
| Red | New texture pixels are uploaded at an observable upload boundary. |

Do not flash for every IMGUI Repaint, cached blit, or unchanged draw call.
Record completed work at its owner, not merely an invalidation request.
Include a short reason and distinguish row updates from full-panel rebuilds.

The terminal paints changed content into a RenderTexture. GPU drawing into that
texture is not a CPU-to-GPU pixel upload. Report this separately as a pixel-cache
repaint. Include the affected rows or area and the reason. Define a separate visual
marker during implementation. Do not label this work as a red upload. Do not infer uploads
from font requests when Unity's actual upload boundary is unobservable.
State instrumentation coverage in the legend so absence of red is not evidence
that Unity performed no uploads.

Colors describe work categories, not measured cost. CPU time, allocations, layout
rebuild counts, and pixel-cache repaint counts remain separate diagnostics.

## Ownership and implementation

- Add a runtime debug toggle, disabled by default, using the existing debug/settings
  conventions. Disabling clears pending flashes and bypasses event collection.
- Terminal cache and row-run owners report their own updates. Sidebar owners report
  layout or membership rebuilds without changing their invalidation rules.
- Keep diagnostic storage bounded and reusable. Start with 100 ms flashes using
  monotonic wall time, a 200-event cap per Unity frame across IMGUI passes, and a
  200-active-overlay cap. Coalesce repeated regions and expose dropped-event counts.
- Paint translucent highlights or outlines in a separate final overlay pass,
  outside terminal pixel caches. The overlay must neither invalidate observed
  content nor report its own drawing as work. Preserve input and control IDs.
- Resolve regions through current panel geometry and clipping. Clear events when
  their owner closes or changes identity. Hidden panels must not leave stale marks.
- Keep overlay overhead bounded. Do not rebuild reason strings on each draw or
  retain session contents for diagnostics. Optional counters and reasons should
  use stable categories.

## Completion requirements

Test expiration, coalescing, frame/active caps, disabled behavior, and owner cleanup
with game-free tests where practical. Run `make test-mod` and `make lint-mod` for
the implementation. Use the [trace workflow](ops-diagnostics.md) to compare overlay
overhead under matching conditions. Collection counts are not allocated bytes.

In-game checks require a separate user request. Verify idle terminals settle,
sparse edits mark the affected rows, and full invalidations report their cause.
Cover resize, scrolling, theme/font changes, alternate-screen transitions, split
panes, covered/closed panels, and sidebar membership changes. Cursor and selection
activity must not force terminal text repaint. Check that flash expiration works
while simulation ticks are stopped and that input remains unchanged.

Document runtime checks not performed. On completion, update the focused ownership
notes and user-facing debug instructions, then delete this plan.
