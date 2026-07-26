# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

## Interface

- [ ] New window: "Projects". First button in the bottom bar. Every project contains: project directory, sandbox params (presets like "Claude", "dbus", "systemd", plus optional custom additions). Agents now must belong to specific project on creation and inherit it params. Stays configurable per agent: name and command.
- [ ] New agent action button: "Duplicate". Opens agent creation window with fields filled with existing agent's values.
- [ ] Render session/weekly limits as generic RimWorld resources: icon + white % text. Show more info on hover like now.
- [ ] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)

## Terminal

- [ ] Bug: copy doesn't work (Claude tmux integration?)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [ ] Bells and whistles
  - [ ] Color schemes
  - [ ] Cursor color
  - [ ] OSC8/URL hyperlinks

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [ ] Tune plague odds and spread rate
- [ ] Replace loading screen tips. Start with lorem ipsum list.

## Sound and music

- [ ] Bug: no music after bg1 stops playing (not sure, need to check)
- [ ] Human: replace SFX
- [ ] Human: 2-3 more bg songs

## Agents support

- [ ] OpenCode support
- [ ] pi support

## Docs

- [ ] Clanker notes in `docslop/`
- [ ] Human notes in `docs/`
- [ ] Attribution: game creators, mod libraries, freesound samples. Text and in-game.

## Misc

- [ ] Create a separate game "profile" with separate saves and all mods/DLCs disabled except ours. Runner script/binary.
