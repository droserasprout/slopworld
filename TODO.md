# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

Remove ticked bullets when verified.

## Interface

- [ ] Move "Shortcuts" from "Agents" view to bottom bar.
- [ ] In usage resources top left credits show left instead of spent
- [ ] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)
- [ ] Cleanup: drop items that don't affect our "game" from Settings window if it can be modded.

## Terminal

- [ ] Bug: thin black line between every 4 green or red lines (diff in CC)
- [ ] Bug: copy doesn't work (Claude tmux integration?)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Add terminal title to title bar (below basic terminal info)
- [ ] Don't minify colonists top center bar. Render it as is and grow title bar accordingly.
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [ ] Color schemes. Default to dark RimWorld-style pallette.
- [ ] Bells and whistles
  - [ ] Cursor color
  - [ ] OSC8/URL hyperlinks

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [ ] More thicc pink smoke when plague tags or takes effect.
- [ ] When mouse clicking make cursor rotate a little. One time and not so intense as when petting a cat.
- [ ] Bug: day/night in game is not synced with real time in user's timezone
- [ ] Cat emits good aura: tiles in small radius cleanes, flora and fauna get temporary cleanse and immunity from plague.
- [ ] Human: slopify main background with SD or something
- [ ] Human: tune plague odds and spread rate

## Sound and music

- [ ] Bug: no music after bg1 stops playing (not sure, need to check)
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

- [ ] Centralized community workshop of templates

___

## After 0.1 release
