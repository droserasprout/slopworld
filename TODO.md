# TODO

Loosely sorted by priority. PRs are welcome.

This file is for humans. If you're a clanker - go to `docslop/`.

## Interface

- [x] New window: "Shortcuts". Two kinds: prompt and shell. Prompt creates new temporary clanker from template, pastes text to the input field, sends Enter. Shell spawns new clanker with interactive shell and sends text to shell. Both clankers disappear as soon as underlying process exits.
- [x] New window: "Projects". First button in the bottom bar. Every project contains: project directory, sandbox params (presets like "Claude", "dbus", "systemd", plus optional custom additions). Agents now must belong to specific project on creation and inherit it params. Stays configurable per agent: name and command.
- [ ] In usage resources top left credits show left instead of spent
- [x] New agent action button: "Duplicate". Opens agent creation window with fields filled with existing agent's values.
- [x] Render session/weekly limits as generic RimWorld resources: icon + white % or $ text. Show more info bubble on hover like now. Suggest icons, builtin or generated.
- [x] Hide enabled mods and DLCs block on loading screen
- [ ] Suppress the "unsaved work will be lost" confirmation now that quitting always saves.
- [ ] "Tab / Shift+Tab in floating windows to navigate between fields (might be hard to implement)
- [ ] Cleanup: drop items that don't affect our "game" from Settings window if it can be modded.
- [x] Loading screen: hide loading block, the one above tips. Add tip "Press `F12` to toggle terminal". Make tips change faster to show 4-5 during game loading.

## Terminal

- [x] Bug: after "New colony" terminals have fixed size when opening first time with "Terminal" action
- [ ] Bug: thin black line between every 4 green or red lines (diff in CC)
- [ ] Bug: copy doesn't work (Claude tmux integration?)
- [ ] Add "+" button to the small floating colonist panel
- [ ] Add terminal title to title bar (below basic terminal info)
- [ ] Bug: DnD text selection highlights the whole line under the cursor including empty space
- [ ] Remove "Stop" and "Restart" buttons, turn "Close" button into red cross.
- [x] Move small colonist panel higher over the title bar (make title bar a bit taller, just to fit colonist icon, leave the name label hang below edge)
- [ ] Color schemes. Default to dark RimWorld-style pallette.
- [ ] Bells and whistles
  - [ ] Cursor color
  - [ ] OSC8/URL hyperlinks

## Sandbox

- [ ] Bind `/run/user/$UID/systemd` that makes `systemctl --user` work.

## "Game"

- [x] When hovering compute core, show bubble with random loading screen tip
- [x] Bug: "Claudwatching" is a typo in one of generic pawn behaviors when idling. They should do usual shit idling pawns do in vanilla game.
- [ ] More thicc pink smoke when plague tags or takes effect.
- [x] Bug: clankers have human faces on game load until running "New colony"
- [x] Remove rule "clankers sleep when terminal is idle". Let them do noting and "claudwatch" as some idle action named. Show normal idle icon (clock). But add gentle sound when agent becomes idle. Something from core sounds. Also, enforce "run in background" setting.
- [ ] When mouse clicking make cursor rotate a little. One time and not so intense as when petting a cat.
- [ ] Bug: day/night in game is not synced with real time in user's timezone
- [ ] Cat emits good aura: tiles in small radius cleanes, flora and fauna get temporary cleanse and immunity from plague.
- [x] Skip the welcome message window when creating a new colony.
- [x] Plague should suppress creating new plants in its radius. Animals and humans keep spawning, but outside walk on map edges.
- [x] When petting cat by clicking, rotate cursor CCW then back for a couple of times.
- [x] Replace loading screen tips with #loading-tips
- [x] Make initial sequence more cinematic: Disable clicking pawns while UI is hidden. Scenario: humans and animals placed. Colonists and cat drop from the sky and start walking. Computer core drops from the sky. After a couple of seconds it starts emitting thick pink fumes of plague. Colonists explode, plague starts spreading. Clankers appear with thick pink fumes each. A couple of seconds. Then UI appears.
- [ ] Human: slopify main background with SD or something
- [ ] Human: tune plague odds and spread rate

## Sound and music

- [ ] Bug: no music after bg1 stops playing (not sure, need to check)
- [ ] Human: replace SFX
- [ ] Human: 2-3 more bg songs (30m+ playtime would be nice)

## Agents support

- [ ] Claude Code
  - [x] Bump default usage interval to avoid 429
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

___

## After 0.1 release
