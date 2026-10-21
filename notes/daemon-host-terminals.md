# Host terminal tabs

Saved host shells are restartable sidebar tabs, separate from sandboxed agents and
[temporary errands](daemon-library.md). Their records retain identity, project, last
observed directory, and autostart policy. They have no colonist or private sandbox
state; exit evidence goes to the daemon journal.

`application/host_records.rs` owns registration and path edits through shared
configuration transactions. `session/host_metadata.rs` polls live directories and
foreground processes. Inspect tmux outside the session boundary, then recheck run
identity before applying observations. Failed saves leave accepted records unchanged;
later observations retry even if the live path already matches.

Tmux metadata recovers surviving shells after daemon replacement. Without a pane,
saved configuration restores either an autostarted shell or a stopped tab. Renames
retain identity, and loading never allocates replacement IDs. The last observed
directory supplies the next launch path.

See [configuration](daemon-config-stores.md), [lifecycle](daemon-session-lifecycle.md),
and [sidebar titles](mod-sidebar.md). User actions belong to
[Host shells](../docs/src/terminals/host-shells.md).
