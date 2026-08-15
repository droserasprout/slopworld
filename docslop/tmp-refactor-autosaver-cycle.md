# Refactor: break AutoSaver ↔ QuitInterceptor cycle

Owns: `mod/Source/SlopWorld/Sim/AutoSaver.cs` and
`mod/Source/SlopWorld/Sim/QuitInterceptor.cs`.

The only genuine file cycle in the repo (`tokensave circular`): these two depend
on each other. (The other reported "cycle" spans ~90 files across C#, Rust and
Python — a tool artifact, ignore it.)

Steps:

- Find the shared contract — likely a "quitting in progress" / "save now" flag, or
  a save request one calls on the other.
- Move that seam to a neutral third type: a small static flag holder, or an event
  one raises and the other subscribes to — so the dependency arrow points one way.

Done when: `tokensave circular` reports no cycle between these two files, and
neither `using`/references the other's type directly.
