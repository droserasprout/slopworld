# Possible command registry direction

SlopWorld currently has separate palette, keybinding and availability paths. To combine them, start with stable command IDs and shared availability/context predicates.
Then connect menus and keys. Settings schemas are a separate concern. Do not copy a large registry framework
before those shared concepts exist.

The useful VS Code precedent is command-ID-based terminal routing: decide whether a command
belongs to the workbench before sending it to the application. A list of intercepted
keycodes cannot express context or user remapping consistently. This is a design reference,
not an implemented SlopWorld registry.
