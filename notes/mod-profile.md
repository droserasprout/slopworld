# Mod profile isolation

`ModBootstrap` and `ModProfile` gate profile-specific changes on existence of the
launcher-written marker. Harmony registration, startup def mutation, and XML patch
operations have independent gates; each entry point must respect its own guard.
The game can still load SlopWorld definitions while patching is refused.

Assemblies load before XML, and static constructors run afterward. A single Harmony
gate cannot protect the other paths. Launcher profile lifetime belongs to
[profiles](ops-profile.md), simulation integration to [simulation](mod-sim.md),
and direct-launch refusal guidance to [troubleshooting](../docs/src/reference/troubleshooting.md).
