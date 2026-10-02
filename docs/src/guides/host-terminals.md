# Host terminals

The add strip offers host shells at `~` or at a project directory, without an agent pawn.
Project host tabs remain after their shell stops. You can restart these tabs.
They save the last working directory and use it at the next launch.
After a reboot, saved project tabs start automatically when autostart is enabled
(the default). Otherwise, they return as stopped tabs.

The context menu offers Start, Stop, Terminal, Label, and Remove. Terminal is available
only while the pane is running. Stop ends the shell but keeps the tab and its saved
directory. Remove ends the shell and deletes the saved tab. Agent edit and duplicate
actions do not apply to host tabs.

Host tabs use the terminal application's title. Label saves a fixed title; clearing
it restores the application's title.
