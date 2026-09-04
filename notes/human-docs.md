# Docs (`docs/`)

mdBook, `make docs` / `make docs-serve`, output gitignored under `docs/book/`.
Agents may write prose there when explicitly asked. This note is source material
for the book, not a draft to copy without checking the current implementation.

## Who owns which fact

- Tour pages are short narrative introductions. No option tables, config keys,
  or troubleshooting.
- Each fact has one canonical topical page. Tours and the FAQ link to that page
  instead of repeating reference material.
- FAQ contains recurring questions rather than serving as the complete reference.
- Give FAQ questions explicit anchors (`### ... {#not-patched}`) so wording can
  change without breaking tour links and bookmarks.
- Troubleshooting is one page: `reference/troubleshooting.md`. A "Known limitations"
  heading at the bottom groups limitations that are not actionable symptoms.
- Integration reference material lives in `reference/integrations.md`.
- macOS has its own guide at `guides/macos.md`, not a section in `install.md`.
- Sidecar worker setup has its own guide at `guides/sidecar.md`; keep it separate from
  the macOS client workflow.
- `reference/api.md` is the user-facing wire protocol reference sourced from
  [wire-protocol](wire-protocol.md).

## Candidate material per page

Interface - short intro, the sidebar, files/search/git views, the terminal, usage
readout in the top bar, eco mode, and a link to keys. Sources:
[mod-ui-chrome](mod-ui-chrome.md), [mod-sidebar](mod-sidebar.md),
[mod-terminal](mod-terminal.md), [mod-content-views](mod-content-views.md),
[mod-eco](mod-eco.md).

Sandboxing - bubblewrap, one preset file per piece of software, `global.toml` as
the implicit system preset, protected paths (`/`, `$HOME`, daemon config, preset
directory, session state root), private state, project directories always
read-write, the `escapes` warning. The README's backup warning belongs here in
full. Sources: [sandbox-isolation](sandbox-isolation.md),
[daemon-presets](daemon-presets.md), [agent-grants](agent-grants.md).

Fun - the jukebox and what it plays, the dead ground, what a working agent
builds, skyfallers, and agent titles. Sources: [mod-jukebox](mod-jukebox.md),
[mod-jukebox-library](mod-jukebox-library.md),
[mod-plague](mod-plague.md), [mod-worksite](mod-worksite.md),
[skyfallers](skyfallers.md), [agent-titles](agent-titles.md).

## Open decisions

- README keeps a short Linux quickstart and links to the book for requirements and
  platform-specific installation.
- Images can be added later under `docs/src/images/`; none are required for the
  first revision.
- `docs/book/` is gitignored.
