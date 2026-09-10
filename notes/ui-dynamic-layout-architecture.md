# Dynamic UI architecture

Workspace geometry, left/right navigation, density presets, the pure row/column
composition layer, Settings migration, and single-slot panel ownership are implemented.
Splits and the broader style architecture below remain proposed.

`SandboxPage` now applies the same pattern to a master/detail view. A small pure policy
keeps its list and editor side by side when their minimum widths fit, stacks them when
they do not, and bounds both panes inside very small viewports. The page retains its
selection, live edits, and two scroll owners when placement changes.

Settings pages share bounded body/footer geometry and frame-stable form measurement.
Audio and Statusbar scroll; Terminal shares Appearance's cached form/preview composition
and moves its preview into the scroll content in short windows. Daemon forms retain
their save/load state and overlays. Key bindings, Storage actions, Binaries, Usage,
custom command fields and About credits adapt their rows or columns at narrow widths.
Storage and Binaries captions scroll with their content in short viewports.
Geometry and measurement are tested without Unity. The user reported no regressions
after checking the Settings migration in-game.

## Workspace and panels

A workspace shell owns navigation, panel placement, active content, and focus. A
`WorkspaceLayout` computes one geometry snapshot consumed by drawing, hit testing,
terminal sizing, and Harmony integration. Placement policy is separate from rendering.

`IContentView` extends `IWorkspacePanel`: instance identity, minimum size hints, assigned
bounds, visibility and focus lifecycle. `WorkspacePanelOwner` owns the active content and
retains a backing terminal without closing it when covered. `TerminalPanel` owns session
and input state; the window still supplies terminal rendering/history services. Resize
negotiation uses assigned panel bounds, with no static last-used terminal size. New sessions
use the current host slot, or the workspace content slot when no host exists.

Panel focus follows host input eligibility. Losing terminal focus releases forwarded mouse
gestures and queued input. Field focus still uses IMGUI; explicit Tab/Shift+Tab traversal,
field-focus restoration and inter-panel keyboard navigation remain a separate slice. Tab
inside a terminal belongs to its application. Multiple visible terminals still require
extracting rendering/history services and choosing how duplicate session views negotiate size.

## Layout and rendering

Build a small composition layer: row, column, split, stack, and scroll. Support fixed,
content, and flexible sizing with minimum sizes, padding, gaps, and alignment. Measure
content, assign rectangles, then use existing IMGUI controls to draw and handle events.
Keep geometry stable across related IMGUI event passes and preserve control IDs.

Retain `Slab`, shared widgets, `SmoothScroll`, pixel snapping, and visible-row rendering.
Cache measured layout by available space, content revision, and text/metric revisions;
avoid rebuilding an allocated tree on every event. Specialized terminal and Markdown
renderers continue to own their internal layout.

## Styling

Resolve explicit style objects through a `UiContext`:

- `ThemeColors`: semantic colors supplied by existing `UIScheme` palettes.
- `ThemeMetrics`: spacing, padding, density, borders, and minimum control sizes.
- `ThemeTypography`: font roles and measurement inputs.
- `ControlStyle`: normal, hover, pressed, focused, selected, and disabled appearance.

Allow scoped metric/style overrides, such as compact navigation with comfortable forms.
Keep ANSI colors in `TerminalTheme` and preserve the existing flat visual language.
Separate paint invalidation from layout invalidation: color changes repaint; fonts,
spacing, and density remeasure. Include font-atlas and UI-scale changes in cache keys.

## Configuration and migration

Start with typed settings for side, width, visibility, and density. Later introduce a
versioned layout description for presets and optional splits, with validated minimum
sizes, fallback defaults, and reset. Keep workspace preferences in the mod settings
store; the daemon remains responsible for session state and terminal resize negotiation.

Migrate geometry first, then one settings page to layout composition, then remaining
views and panel ownership. Add docking only after presets and splits demonstrate a need.
Validate layout mathematics independently of Unity; verify runtime input and rendering
when game execution is explicitly authorized.
