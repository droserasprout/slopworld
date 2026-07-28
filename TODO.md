# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

Remove ticked bullets when verified.

## Interface

- [ ] Add the first resource to panel: time (some icon and HH:mm, full with date on hover)
- [ ] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)
- [ ] Cleanup: drop items that don't affect our "game" from Settings window if it can be modded.
- [ ] Next planet cinematics: disable UI, pause a little before bombing, bomb in multiple waves with a little pause. Then drammatic pause. Then new game.

## Terminal

- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [ ] Bug: copy selection requires holding Shift (Claude Code, OSC 52)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Add terminal title to title bar (below basic terminal info)
<!-- - [ ] Don't minify colonists top center bar. Render it as is and grow title bar accordingly (a little). Also resources (== usage) drawn above terminal. -->
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Color schemes. Default to dark RimWorld-style pallette.
- [ ] Cursor color
- [ ] OSC8/URL hyperlinks
- [ ] RMB menu

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [ ] New game cinematics: skip dropping 3 initial colonists in pods (and exploding them later). Instead, if clankers are configured, drop them in pods after computer core starts emitting plague. When new clanker is added it's also dropped in pod from the sky.
- [ ] Don't pause the game while main menu (Esc or habburger one) is open
- [ ] Bug: When clanker is down, Strip action button shown in it's menu.
- [ ] Lights
- [ ] Workplaces
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
