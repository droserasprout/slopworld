# Refactor: SandboxPreviewPanel field table

Owns: `mod/Source/SlopWorld/UI/SandboxPreviewPanel.cs`.

`Build` has the highest fan-out in the repo (38). About twelve of its lines are
the same shape:
`data.Fields.Add(new SandboxPreviewField(label, Merge(presets, p => p.X)))`.

Steps:

- Drive the repeated adds from a table of `(label, Func<Preset, List<string>>)`
  pairs and loop over it.
- Keep the specials explicit, outside the loop: resource limits (agent-gated),
  `FinalEnvironment(presets)`, and the read-only/read-write pair (rw subtracted
  from ro — see the bwrap ordering comment).

Pure data-shuffle. The effective-network and effective-DNS resolution above the
field list stays as is.

Done when: the twelve near-identical `Fields.Add` lines collapse to one loop.
