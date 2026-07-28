# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

Remove ticked bullets when verified.

## Interface

- [x] Add time as first resource on top left panel (any related stantard icon and HH:mm label, full with date on hover in bubble)
- [x] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)
- [ ] Cleanup: drop items that don't affect our "game" from Settings window if it can be modded.
- [x] Next planet cinematics: disable UI, pause a little before bombing, bomb in multiple waves with a little pause. Then drammatic pause. Then new game.

## Terminal

- [x] Remove "Stop" and "Restart" buttons, turn "Close" button into white/grey cross.
- [x] Bug: copy selection requires holding Shift (Claude Code, OSC 52)
- [x] Add terminal title to title bar (below basic terminal info)
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [x] RMB menu (basic actions)
- [ ] Color schemes. Default to dark RimWorld-style pallette.
- [ ] Cursor color
- [ ] OSC8/URL hyperlinks

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [x] New game cinematics: Cat spawns somewhere on map at the beginning instead of dropping in pod.
- [x] Plague fx: randomize speed and color slightly (slower, more violet)
- [ ] Don't pause the game while main menu (Esc or habburger one) is open
- [ ] Bug: When clanker is down, Strip action button shown in it's menu.
- [ ] Lights
- [ ] Workplaces
- [ ] Human: tune plague odds and spread rate
- [ ] Alt+Num focuses clanker in main game view like in terminal

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
