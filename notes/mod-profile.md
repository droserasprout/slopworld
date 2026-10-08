# Mod profile isolation

`ModBootstrap` and `ModProfile` gate profile-specific changes on existence of the
launcher-written marker. Harmony registration, startup def mutation, and XML patch
operations have independent gates; each entry point must respect its own guard.
The game can still load SlopWorld definitions while patching is refused.
On refusal, the mod disables itself in the current profile's saved mod list and
installs only a Root.Update hook to show a mandatory quit dialog once the window
stack exists. Disabling takes effect on the next launch; the loaded session cannot
continue into ordinary play. A newly enabled mod cannot intercept the Mods screen
before the restart that loads its assembly.

The mandatory startup refusal owns access to play. Game/map component callbacks
and gameplay UI rely on that boundary rather than repeating profile checks.
Startup def mutation and XML patch gates remain necessary because they run before
the refusal dialog can be displayed.

Assemblies load before XML, and static constructors run afterward. A single Harmony
gate cannot protect the other paths. Launcher profile lifetime belongs to
[profiles](ops-profile.md), simulation integration to [simulation](mod-sim.md),
and direct-launch refusal guidance to [troubleshooting](../docs/src/help/troubleshooting.md).
