# SlopWorld - Claude Code

Read this file before acting. [AGENTS.md](AGENTS.md) is the project map every agent
shares; this file is what Claude Code specifically gets wrong here.

## Before writing prose

Read [docslop/prose-guide.md](docslop/prose-guide.md) before adding or editing any
source comment or devnote.

A source comment is one sentence and at most two physical lines. Matching the density
of the comments already around the code is not a defence: much of the existing prose
predates the guide and breaks it. The guide also bans antithesis, trailing flourish,
metaphor in place of the project's own names, and `*emphasis*` markers.

## Before editing

- [docslop/index.md](docslop/index.md) lists every devnote. Check it before deciding a
  fact is unrecorded.
- [docslop/house-rules.md](docslop/house-rules.md): commit on `main`, no branch, no PR.
  `AGENTS.md` is never edited or appended to.
- A fact has one home. When it already lives in a devnote, link to it instead of
  restating it.

## Before deleting anything

You may be running as an agent inside a SlopWorld sandbox on this same machine. `$HOME`
there mixes private per-session copies with host-owned `shared` files, and the two are
indistinguishable by path - see
[docslop/sandbox-blast-radius.md](docslop/sandbox-blast-radius.md).

Read `/proc/self/mountinfo` before deleting under `$HOME`. Sandboxing is not a reason
to run a destructive command you would not otherwise run.

## Verifying work

`cargo test` has four pre-existing failures unrelated to any change: two in `game::`
that depend on host `pgrep`/uptime, and two in `sandbox::` that assert on a session
name where the code now uses a `state_id` uuid. Compare counts against a clean tree
rather than assuming a regression.

`make mod` uses direct Mono `csc` because this machine's msbuild can resolve the
Roslyn compiler wrongly; override `CSC` or `CSC_API` when those paths differ.
