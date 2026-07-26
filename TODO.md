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

- [ ] Bug: thin black line between every 4 green or red lines (diff in CC)
- [ ] Bug: copy doesn't work (Claude tmux integration?)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [ ] Move small colonist panel higher over the title bar (make it a bit taller, just to fit colonist icon, leave name label hang)
- [ ] Bells and whistles
  - [ ] Color schemes
  - [ ] Cursor color
  - [ ] OSC8/URL hyperlinks

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [ ] Bug: day/night in game is not synced with real time in user's timezone
- [ ] Skip the welcome message window when creating a new colony.
- [ ] Plague should suppress creating new plants in radius
- [ ] Replace loading screen tips. Start with lorem ipsum.
- [ ] Make initial sequence more cinematic: Disable clicking pawns while UI is hidden. Scenario: humans and animals placed. Colonists and cat drop from the sky and start walking. Computer core drops from the sky. After a couple of seconds it starts emitting thick pink fumes of plague. Colonists explode, plague starts spreading. Clankers appear with thick pink fumes each. A couple of seconds. Then UI appears.
- [ ] Human: slopify main background with SD or something
- [ ] Human: tune plague odds and spread rate

## Sound and music

- [ ] Bug: no music after bg1 stops playing (not sure, need to check)
- [ ] Human: replace SFX
- [ ] Human: 2-3 more bg songs (30m+ playtime would be nice)

## Agents support

- [ ] OpenCode support
- [ ] pi support

## Docs

- [ ] Clanker notes in `docslop/`
- [ ] Human notes in `docs/`
- [ ] Attribution: game creators, mod libraries, freesound samples. Text and in-game.

## Misc

- [ ] Create a separate game "profile" with separate saves and all mods/DLCs disabled except ours. Runner script/binary.
