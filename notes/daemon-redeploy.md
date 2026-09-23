# Redeploy invariants

Tmux panes and the game must survive daemon replacement. The tmux server normally runs in
a separate transient service.
Tmux becomes a daemon, so set `Type=forking` and check its socket. Set `KillMode=process`
for slopd. See the service and installer code before changing process ownership.

Rebuilding an emulator needs both captured content and mode restoration via redraw. Initialize primary scrollback separately from visible rows in the alternate screen.
Putting all rows on either side loses history or mode. Restore OSC titles separately because they are not screen content.

Tmux carries durable activity, host-tab and worker identity across daemon-only restarts.
Those options take precedence over disk fallbacks.
Renaming rebuilds reader and input handles that depend on the name.
Size negotiation must check the actual tmux size again after redraw requests.

All windows use the same name. Tmux resolves window names before session names.
Window and pane targets need `name:` or `name:.0`.
Names without these suffixes are safe for session operations only.
