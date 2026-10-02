# Shared UI scrolling

`UI/Scrolling/` owns `SmoothScroll`, fractional wheel input, scrollbars, and shared
wheel routing. Native sampling belongs to `UI/Scrolling/Platform/`. The profile's
Smooth scrolling preference controls native sampling and applies live.

Callers must consume wheel input once, preserve nested scroll ownership, and use
identical viewport clipping for drawing and hit testing. Drag owners respect
`hotControl`. Wheel-only passes may reuse measured extents; resize requires fresh
measurement. `ContentHeight` publishes shared listing measurements on the next
frame so input and repaint use consistent geometry.

`ScrollWheelRouter` captures and replays shared scroll regions. Hosts must invalidate
its retained snapshot on closure. Settings page geometry belongs to
[Settings](ui-settings.md), and keyboard-focused field reveal to [focus](ui-focus.md).
Backend sampling and event-compaction mechanics stay with source comments and tests.
