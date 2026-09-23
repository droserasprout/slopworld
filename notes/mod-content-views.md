# Content and window ownership

`TerminalWindow` hosts `WorkspacePanelOwner`: a retained `TerminalSplit` plus optional
covering content. Hiding backing terminals is not closing them. `Showing` describes the
covering content, not the focused terminal pane. See [terminal](mod-terminal.md).

Options renders its off-stack `Dialog_Options` inside the host. Vanilla checks for a currently
drawn options window must account for that embedded path. The main menu still uses a real
window, so placement patches must distinguish both paths. Both close paths persist settings
and release page instances.

Dialogs opened above fullscreen chrome need Super-layer promotion.
Otherwise, the terminal paints over them. Views must claim their keys before forwarding to the backing agent.
