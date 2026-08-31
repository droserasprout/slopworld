# Content views

`TerminalWindow` owns the chrome: sidebar, top bar and the remaining body. An
`IContentView` fills that body. The pane itself is not a content view; `_content ==
null` means the window draws an agent, host shell or pager directly.

The current views are `OptionsView`, `SessionsView`, `ProjectsView` and
`LibraryView`. `TerminalWindow.Showing` owns the one active body;
`ToggleContent<T>` opens/switches it and `ShowingAs<T>` queries it.

- Opening a view over a pane keeps the session; `Leave` restores the pane or closes
  the window if there was none.
- Escape leaves a view and is forwarded to a pane's agent; F12 closes from either
  state. `ChromeKeys` owns both, so views do not leak input to agents.
- Dialogs opened while the fullscreen chrome is up must be on the `Super` layer.
  `OpenOverPane` handles dialogs opened by the mod, and `Patch_DialogsOverChrome`
  handles vanilla dialogs opened from pages. Main tabs remain under the pane.
- The top bar names the current view instead of the agent.

This structure is also an input fix: an absorbing window below the chrome never gets
`MouseDown`, so drawing Options inside the chrome left the sidebar visible but dead.
Nothing now absorbs above the chrome.

## `OptionsView`

The pages still use vanilla `Dialog_Options` layout, but the view keeps the dialog
off the window stack and calls `DoWindowContents` on the body rect. `OptionsView.Anywhere`
replaces checks for `currentlyDrawnWindow is Dialog_Options`, including OK suppression
and stripped vanilla rows.

The main menu still uses the real options window, so its size/place/band patches remain;
the band patch skips itself while the chrome is drawing the pages. `SlopOptions.Teardown`
drops cached pages and is called on either close path. The last selected category is
remembered in memory; explicit `OpenCategory` requests win.
