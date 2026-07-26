# TODO

Loosely sorted by priority. PRs are welcome.

## USER

- [ ] Cleanup
  - [ ] Disable resources spawn on the map (via scenario?)
  - [ ] Disable right-click/Tab menu on map (empty selection)
- [ ] Interface
  - [ ] Colonist selected: square action button "Start/Stop". Stop shows confirmation dialog that process will be killed.
- [ ] Terminal
  - [ ] Bug: DnD text selection highlight the whole line under the cursor including empty space
  - [ ] Bells and whistles
    - [ ] Color schemes
    - [ ] Cursor color
    - [ ] OSC8/URL hyperlinks
- [ ] "Game"
  - [ ] Bug: colonists' pets not exploding and not affected by plague
  - [ ] Tune plague odds and spread rate
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

## AGENT

### Milestone: develop SlopWorld from inside SlopWorld

The loop we want: an agent in a session edits this repo, runs `make redeploy`,
the daemon and the mod come back, and the same colony with the same agents is
on screen seconds later. Groups A and B below are done; C and D are not.

- [x] Survive a daemon redeploy with the agents still running
  - [x] Bug: `make install-daemon` killed every agent. `slopd.service` has no
        `KillMode`, so it defaults to `control-group`, and the tmux server was a
        child of slopd - so it sat in slopd's cgroup and `systemctl --user
        restart` SIGTERMed it along with every sandbox under it, including the
        session that ran `make`. `Tmux::ensure_server` now starts the server
        under `systemd-run --user --scope` as `slopworld-tmux.scope`, with an
        inline fallback where there is no systemd. The first redeploy after this
        ships still costs the agents: the server running right now is the one
        already in the old cgroup.
  - [x] Scrollback died with the daemon even when the session lived. Now
        `history_limit` in `[daemon]` (5000) is set globally before any pane
        exists, and `spawn_reader` seeds the emulator from
        `capture-pane -e -S -<history_limit>` instead of the visible pane alone.
  - [x] Terminal modes after a reattach (alt screen, app mouse, cursor shape)
        are not in a text capture. `Tmux::nudge_redraw` bounces the window one
        column and back on the adoption path; the SIGWINCH makes the app repaint
        and hand the fresh emulator its modes.
  - [x] tmux sessions with no config entry are now logged as a warning at
        startup instead of being silently invisible.
  - [x] Reconnect backoff capped at 5s, down from 30s: the usual reason the
        socket dies is a two-second daemon restart. Manual reconnect was already
        in mod settings.
  - [x] Keys typed while the socket is down are counted and reported in a banner
        over the pane, instead of vanishing. Not buffered on purpose - keys held
        over a reconnect would land in whatever the TUI is showing by then.
  - [x] `config.toml` is re-read whenever its mtime moves (2s check, and ahead of
        every mutating call), so a hand edit both takes effect and survives the
        next write from the GUI. `SlopConfig.ToJson` carries the new `[daemon]`
        fields, which is the other half of not clobbering it.
  - [ ] Adopt an orphan tmux session into config rather than only logging it.
        Needs a guess at the agent command and the sandbox flags, which is why it
        is not done.
  - [ ] `/api/health` already carries the daemon version; nothing reads it.
        Surface it in-game so "the daemon restarted under you, and it changed"
        is visible rather than inferred.
  - [ ] A session must not be able to kill itself by accident.
        `SLOPWORLD_SESSION` is already in the environment (`sandbox.rs`); refuse
        stop/restart of the calling session unless forced.
- [x] Survive a mod redeploy with the same colony
  - [x] `AutoResume` loads the newest save on launch rather than stopping at the
        menu - once per process, and only when no game is loaded, so quitting to
        the menu leaves you there. Setting: "Resume the newest colony on launch".
  - [x] `AutoSaver` saves every `autosaveMinutes` real minutes (default 2) off
        the game tick, plus prefixes on `Root.Shutdown` and
        `GenScene.GoToMainMenu`. RimWorld's own interval is in game days, about a
        quarter of an hour at 1x, which is far too coarse to restart against.
  - [x] Loading a save does not replay the opening - already true on inspection.
        `IntroDirector` scribes its phase and forces it to Done past
        `FreshGameTicks`; `Plague` scribes origin, radius and active.
  - [x] `TerminalRecall` puts the open terminal back after a restart, scribed
        into the save (it belongs to a colony) and waiting for the session to
        report in before opening. Scroll position is not kept: the emulator is
        reseeded from tmux, so there is nothing to scroll back to yet.
  - [x] "Save and restart" from inside the game: `POST /api/game/restart` starts
        `daemon.game_cmd` as a transient systemd unit after a delay, and the
        Config window's daemon tab saves the colony, calls it and quits.
  - [x] `AgentColony` reconciles against pawns already in the save - already
        true: `FindExisting` matches an untracked colonist by name before
        `Spawn` is reached.
  - [x] `make redeploy`: install both halves, then ask the daemon to bounce the
        game. One command, from inside a session.
  - [ ] Suppress the "unsaved work will be lost" confirmation now that quitting
        always saves. Needs the game's own string keys checked with ikdasm.
- [ ] Let a sandboxed agent actually build and deploy
  - [x] Widen the binds. All config, no daemon rebuild: `ro_paths`, `rw_paths`
        and the session's own `rw_paths` were already there. Split by scope on
        purpose - read-only paths any agent can have go in `[sandbox]`, the
        writable ones go on the dev session, because a binary on `PATH` or a
        live dbus socket is the run of the user account. Binds are built at
        spawn, so none of it takes effect until the session restarts.
    - [x] `~/.local/bin` was read-only (`Sandbox::default`), so `install -Dm755`
          of the daemon failed. Same for `$(MODS)/SlopWorld` and
          `~/.config/systemd/user`; `$MANAGED` needs to be readable, and without
          it the mod cannot even be compiled from inside a session. A global
          read-only bind can be overridden read-write on one session: bwrap
          takes the last bind for a path, and `rw_paths` are pushed after
          `ro_paths` in `build_argv`.
    - [x] Toolchain: cargo and rustup state (`~/.cargo`, `~/.rustup`) and
          msbuild's package cache (`~/.nuget`, which `-restore` writes), or
          every build is cold and needs the network.
    - [x] `~/.gitconfig`, or a session cannot commit without being handed a
          repo-local `user.name`. `.git/config` in this repo carries one; drop
          it now the bind has landed.
    - [ ] `/run/user/$UID/systemd`, which is the bind that actually makes
          `systemctl --user` work. `/run/user/$UID/bus` alone is not enough and
          reads as though it should be: `busctl --user list` succeeds over it
          while every systemctl call fails on ENOENT, because for user scope
          systemctl does not use the session bus at all - it opens systemd's
          private socket under `$XDG_RUNTIME_DIR/systemd`, which is what its
          error means by "local transport". Bind it rw; connecting to a unix
          socket is a write. Not the whole of `/run/user/$UID` - that is gnupg,
          gcr and keyring, i.e. every secret the user has.
          `XDG_RUNTIME_DIR` and `DBUS_SESSION_BUS_ADDRESS` need nothing: bwrap
          is built without `--clearenv`, so slopd's whole environment is
          already inherited and `pass_env` only decides what is overridden.
  - [x] The agent cannot see its own crashes - Player.log's directory is bound
        read-only now, which is the config-only half.
  - [ ] `GET /api/gamelog?tail=N` and the daemon's own log, so "patching
        incomplete" is one call rather than a path every agent has to know.
  - Note: `~/.config/slopworld/` is deliberately *not* bound. An agent that can
    rewrite its own sandbox rules does not have one, so every change here has to
    come from outside the session.
  - [ ] Per-session `ro_paths`. `SessionCfg` has `rw_paths` but no read-only
        counterpart, so `$MANAGED` and Player.log had to go in the global
        `[sandbox]` - harmless, both being read-only and inert, but the scoping
        is by accident rather than by choice.
  - [ ] The agent cannot see the UI it is changing. `POST /api/screenshot`,
        handled on the mod's main thread, writing into the repo - the single
        biggest one for any UI work.
  - [ ] A bad `config.toml` should not take the daemon down where nothing can
        reach it: validate on an `ExecStartPre` check. The running daemon already
        keeps serving the last good config when a reload fails to parse.
- [ ] Make a long session in the terminal bearable
  - [ ] Default 120x34 is small for a coding agent, and the pane size lives in
        config rather than following the window. Confirm the resize round-trip
        persists, or make the window size authoritative.
  - [ ] PageUp/PageDown while scrolled, jump to bottom on keypress, scrollback
        search.
  - [ ] Hotkey to jump to the next agent in Waiting, and a toast (and optional
        sound) when one goes Waiting with its terminal closed.
- [ ] Catch a broken redeploy immediately
  - [ ] `SlopWorldBootstrap` should log "patched N/N" and name every patch whose
        target failed to bind. A moved target is silent today, and the report is
        always "the mod stopped working".
  - [ ] Compare mod and daemon versions at connect and say so in-game; wire
        skew currently degrades to `Down` and looks like a bug.
  - [ ] CI: `make test` plus a mod build. The mod has no test harness, but a
        build that does not compile against a real install should not reach a
        redeploy.
