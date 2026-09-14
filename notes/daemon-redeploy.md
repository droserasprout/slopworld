# Redeploy invariants

Tmux panes and the game must survive daemon replacement. The tmux server normally runs in
a separate transient service; `Type=forking` and socket verification matter because tmux
daemonizes. `KillMode=process` is also required on slopd. See service/installer code before
changing process ownership.

Rebuilding an emulator needs both captured content and mode restoration via redraw. Seed
primary scrollback separately from alternate-screen visible rows; putting all rows on either
side loses history or mode. OSC title is not screen content and must be restored separately.

Tmux carries durable activity, host-tab and worker identity across daemon-only restarts.
Those options outrank disk fallbacks. Renaming rebuilds name-bound reader/input handles;
size negotiation must recheck actual tmux size after redraw nudges.

All windows use the same name. Tmux resolves window names before session names, so window/
pane targets need `name:` or `name:.0`; bare names are safe for session operations only.
