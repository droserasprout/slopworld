# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

Remove ticked bullets when verified.

## Interface

- [ ] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)
- [ ] Cleanup: drop items that don't affect our "game" from Settings window if it can be modded.
- [x] Move "New colony" button from "Agents" to the main menu (one triggered by Esc or the last hamburger button in the bottom bar). Rename to "Next planet". Skip the confirmation. Cinematics before starting new game: disable UI like in initial scene, then burn/explode everything on the map. Wait 5-7 seconds and proceed to creating new game.

## Terminal

- [ ] Bug: thin black line between every 4 green or red lines (diff in CC)
- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [ ] Bug: copy doesn't work (Claude tmux integration?)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Add terminal title to title bar (below basic terminal info)
- [ ] Don't minify colonists top center bar. Render it as is and grow title bar accordingly.
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Color schemes. Default to dark RimWorld-style pallette.
- [ ] Cursor color
- [ ] OSC8/URL hyperlinks
- [ ] RMB menu

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [ ] Bug: When clanker is down, Strip action button shown in it's menu.
- [ ] Bug: day/night in game is not synced with real time in user's timezone
- [ ] Human: tune plague odds and spread rate
- [ ] Alt+Num focuses clanker in main view like a tab in terminal

## Sound and music

- [ ] Human: replace SFX
- [ ] Human: 2-3 more bg songs (30m+ playtime would be nice)

## Agents support

- [ ] Claude Code
  - [ ] Generate per-project CLAUDE.md with instructions, useful and fun
- [ ] OpenCode support
- [ ] pi support

## Docs

- [ ] Clanker notes in `docslop/`
- [ ] Human notes in `docs/`
- [ ] Attribution: game creators, mod libraries, freesound samples. Text and in-game.

## Misc

- [ ] Create a separate game "profile" with separate saves and all mods/DLCs disabled except ours. Runner script/binary.
- [ ] PKGBUILD with .desktop for host

## A++

- [ ] Centralized community workshop of various templates

___

## After 0.1 release
