# Loading screen

`EarlyBootstrap` installs `LoadingScreenHost` from the mod constructor. This same host
owns full-screen loading through bootstrap, scene switches and auto-resume, including
synchronous events that vanilla would draw as a bubble. It acknowledges repaint to
the long-event scheduler and keeps Root waiting while that full-screen session has
queued work, so menu/map UI cannot cover a promoted synchronous event.
Only an interactive menu/play repaint with no queued events
ends the stream; a null current event is not an end-of-load signal. Ordinary in-game
operation windows remain game-owned outside a full-screen loading session.
`LoadingFontAtlas` decodes the
embedded atlas on the game thread, without waiting for mod content loading. Settings
are available before early hooks are installed; tips are also embedded. This path must
not initialize UI fonts or definitions, or show stage labels. Background animation
starts after the first lightweight tip repaint, from the game's already loaded source.
There is no second loading renderer or stream reset at the late bootstrap boundary.

`Bootstrap/StartupAudio` retains the game's sound-grain resolution callbacks for
first use and warms one definition per idle frame after loading. Native audio stays
on the game thread; duration reads and grain consumers demand resolution first.
Callbacks are weakly owned by definitions so reloads can discard old work.
Only decoded byte counts are persisted under the application cache root. Keys include
the audio runtime identity and the current packed-content hash or loose file path,
size and modification time. Cache failures fall back to native probes. Binding checks
protect the callback rewrite and its consumers against game updates.

`Patches/LoadingScreen/` owns tip streaming and vanilla drawing suppression. It uses
the committed ASCII atlas and terminal font preference. Measure before reading
geometry, and reflow retained text on resize even when no words are due.
Keep the stream's coverage proportional to the background's dark area, independent
of UI scale. Streaming rate follows text capacity. Half-line scrolling must retain
the outgoing row until it is fully clipped; reset the offset on reflow.

After `UIRoot_Entry.Init`, the mod closes the localized `SteamClientMissing` message
as if Ignore were selected. This startup-only hook leaves Steam initialization state
and other dialogs unchanged and is registered only in the marked SlopWorld profile.

`LoadingTipPanel` owns the right-aligned full-height frame and clip for every tip draw,
including window-stack loading. Draws use panel geometry; `GameplayTipWindow.WindowSize`
stays game-owned. Visible rows include the final glyph's full height.

Tip selection must not consume gameplay RNG because it runs during map generation.
`LoadingTipSelection` owns fit ranking and recent history; the stream owns geometry
and mode filtering. Reset selection history on a new load or mode change. Quote
markers control Grandma eligibility and are stripped before display. The mod and
daemon embed the canonical `shared/loading-tips.txt` catalog; `#` lines are comments.

Keep tips printable ASCII with newline as a paragraph separator. The glyph baseline,
gutters, and runtime line height must agree with the baker. Regenerate with
`just generate-loading-font`; [asset tests](../tools/assets/test_text_sprites.py)
check the generated metadata and non-ASCII catalog entries.
