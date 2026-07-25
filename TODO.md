# TODO

Loosely sorted by priority. PRs are welcome.

- [ ] Cleanup
  - [ ] Disable resources spawn on the map (via scenario?)
  - [ ] Disable right-click/Tab menu on map (empty selection)
- [ ] Interface
  - [ ] Colonist selected: square action button "Start/Stop". Stop shows confirmation dialog that process will be killed.
- [ ] Terminal
  - [ ] Bug: DnD text selection highlight the whole line under the cursor including empty space
  - [ ] Maximize terminal window (no transparent borders)
  - [ ] Draw smaller copy of top center colonist bar above terminal. Clicking colonist icon switches terminal.
  - [ ] Bells and whistles
    - [ ] Color schemes
    - [ ] Cursor color
    - [ ] OSC8/URL hyperlinks
- [ ] "Game"
  - [ ] When plague tags flora/fauna or takes action, emit visual effect (pink smoke?)
  - [ ] Bug: colonists' pets not exploding and not affected by plague
  - [ ] Bug: new agent can be spawned inside the rock and immobilized.
    - [ ] When choosing start location, avoid: rocky ones, deverts. Prefer: tropics.
  - [ ] Tune plague odds and spread rate
  - [ ] Autosave every 1-2 real minutes (is it cheap?) and on exit, suppressing "unsaved will be lost" message.
  - [ ] Replace loading screen tips. Start with lorem ipsum list.
- [ ] Sound and music
  - [ ] Bug: no music after bg1 stops playing (not sure)
  - [ ] Human: 2-3 more bg songs
- [ ] "Security"
  - [ ] Carefully read bwrap config
- [ ] Misc
  - [ ] Create a separate game "profile" with separate saves and all mods/DLCs disabled except ours. Runner script/binary.
- [ ] Agents
  - [ ] OpenCode support
  - [ ] pi support
- [ ] Docs
  - [ ] Well, docs
  - [ ] Attribution: game creators, mod libraries, freesound samples. Text and in-game.
