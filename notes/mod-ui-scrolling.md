# Shared UI scrolling

`UI/Scrolling/` owns `SmoothScroll`, fractional wheel input, scrollbars, and shared
wheel routing. Native sampling belongs to `UI/Scrolling/Platform/`. The profile's
Smooth scrolling preference controls native sampling and applies live.

Callers must consume wheel input once, preserve nested scroll ownership, and use
identical viewport clipping for drawing and hit testing. Drag owners respect
`hotControl`. Wheel-only passes may reuse measured extents; resize requires fresh
measurement. `ContentHeight` publishes shared listing measurements on the next
frame so input and repaint use consistent geometry.
Open `UiMenu` bounds reserve wheel and native touchpad input for the topmost menu
under the pointer, including padding and menus whose options fit without scrolling.

Root owns wheel queue admission before window and component dispatch. It compacts
compatible runs only for known mod consumers and bounds input dispatch per frame;
retained tails preserve FIFO order across keys, clicks, pointer changes, modifiers,
displays, and reversals. Unknown consumers retain individual packets. Terminal
application counts retain each original packet's rounding and reader policy, with
bounded wire writes. Queue ingestion is linear in new packets; deferred tails are
not rescanned.

Compatible runs retain native fractional motion and carry native coverage across
deferred delivery. Mixed ownership/direction runs use logical movement because X11
samples have no pointer, modifier, or event-order stamps. That decision belongs to
the retained run, not the delivery frame. Native motion already consumed by history
cannot later reach an application after a mode change.

Terminal wheel passes update input/history targets without assembling displayed
views or preparing rendering. Sidebar open-file scrolling reuses measured extents;
agent rows and the top bar omit presentation work. Nested scroll owners still claim
input before their parents spend it and close their groups normally.

`PerfTrace` records whole root GUI time, admitted wheel packet counts, queue work,
and deferred input. Terminal scroll diagnostics count input *frames* separately;
that count cannot establish the number or cost of wheel events within one frame.

Files/Git trees, Search results, Library lists, and open editor/viewer rows reserve
only the scrollbar rail's horizontal inset for painting. The scrollbar retains its
wider invisible mouse target; hover buttons and row click regions inset by
`ScrollbarHitInset` to stay clear of it. Labels and noninteractive tails use the
full painted width.

Scroll groups use `Widgets.BeginGroup`/`EndGroup` so RimWorld's Linux/Steam Deck
pointer repair runs after each coordinate translation. Raw Unity groups bypass
that repair and can displace hover after keyboard input until the mouse moves.

`ScrollWheelRouter` captures and replays shared scroll regions. Hosts must invalidate
its retained snapshot on closure. Settings page geometry belongs to
[Settings](ui-settings.md), and keyboard-focused field reveal to [focus](ui-focus.md).
Backend sampling mechanics stay with source comments and tests. Wheel routing consumes
the game's delivered events; it does not inspect or rewrite the engine's input queue.
