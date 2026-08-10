# Content views

One window, and what fills it. `TerminalWindow` is the chrome - the column down the
left ([mod-sidebar](mod-sidebar.md)), the line across the top, and the room those
two leave - and `IContentView` is whatever has that room. It was a terminal and
nothing else once; everything that used to open as a window *over* the chrome is
one of these now.

- **The pane** - an agent's, the host's shell, the viewer's `less`. Not an
  `IContentView`: the window draws it itself, that being what the window was built
  as, and `_content == null` is what says so.
- **`OptionsView`**, **`SessionsView`**, **`ProjectsView`**, **`ShortcutsView`** -
  the four that are.

## Why, and it is not tidiness

A window under an absorbing one is **never called for a MouseDown**
([gotchas](gotchas.md)). The options menu was laid out inside the chrome - the
column and the line stayed visible beside it - so the column was drawn to keep
taking its own clicks, and could not: it kept its hover and lost every press, the
tabs and the menus and the tree's rows all dead. `ColonistBarStrip.OptionsOpen` was
an exception written for that and worth nothing. The fix is that nothing absorbs
above the chrome any more, so this is a class of bug rather than a bug.

## The rules

- **One at a time.** `TerminalWindow.Showing` is what has the body, null for the
  pane. `ToggleContent<T>` is every door onto a view, which is what makes each of
  them a switch; `ShowingAs<T>` is the one already up.
- **The pane is behind it.** Opening a view over an open pane keeps the session;
  `Leave` puts it back. With nothing behind it, leaving closes the window and the
  map is what was behind *that* - the chrome exists to show something.
- **Escape leaves the view**, where in a pane it belongs to the agent. F12 closes
  the window from either. Both are read in `ChromeKeys`, and nothing typed reaches
  an agent while a view is up.
- **A dialog opened while the chrome is up is promoted to `Super`.** The chrome is
  a fullscreen `Super` window, so a dialog left on the ordinary layer is added
  underneath it - drawn, listening and invisible. `TerminalWindow.OpenOverPane` is
  that answer given by hand, and is what every `Add` in the three list views uses;
  `Patch_DialogsOverChrome` is the same answer for the ones this half never opens,
  vanilla adding its own from inside the options pages (mod settings, the resolution
  confirmation, the language restart). Only the dialog layer is touched: a
  `MainTabWindow` is `GameUI` and belongs under the pane.
- The top bar names the view where it would have named the agent.

## `OptionsView`, which drives a window that is not one

The pages are vanilla's `Dialog_Options`. The view holds an instance that **never
reaches the window stack** and calls `DoWindowContents` on the body rect once a
frame. Vanilla lays that content out in window coordinates - the category column is
a literal `Rect(0, i*50, 160, 48)` - so `Band` is a GUI group and the rect handed
over starts at its corner.

- `OptionsView.Anywhere` is what everything that used to ask
  `currentlyDrawnWindow is Dialog_Options` asks now: the OK suppression, the three
  vanilla rows `StripOptions` drops, the web links. That question stopped answering
  the moment the pages were drawn by the chrome's window.
- **The main menu still opens the real window** - there is no chrome out there - so
  `Patch_OptionsSize`, `Patch_OptionsPlace` and `Patch_OptionsBand` are kept for
  that road, the last of them skipped while `OptionsView.Drawing` or the page would
  be centred inside itself.
- `SlopOptions.Teardown` is the shared way out: pages dropped so the next open
  re-reads `config.toml`, settings written once. `OptionsView.Closed` calls it, and
  so does the `PreClose` patch for the window road.
- **The tab is remembered across a toggle.** `Closed` hands the category to
  `SlopOptions.Remember` on the way out and `Toggle` opens on it, because the view -
  and the dialog holding the selection - is rebuilt every time. In memory only: which
  page you were reading is about this sitting. A page named outright (`OpenCategory`,
  the palette's per-page entries) still wins.
