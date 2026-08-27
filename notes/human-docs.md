# Human docs (`docs/`)

mdBook, `make docs` / `make docs-serve`, output committed under `docs/book/`.
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
- Split `faq.md` into a folder only when one section alone justifies a page;
  mdBook search indexes the whole book, and one long page is Ctrl-F friendly.

## Candidate material per page

Interface - loading screen and intro, colonist bar as live agents, the sidebar,
the terminal window filling the screen opaque, files/search/git views, usage
readout in the top bar, color schemes, eco mode, keys. Sources:
[mod-ui-chrome](mod-ui-chrome.md), [mod-sidebar](mod-sidebar.md),
[mod-terminal](mod-terminal.md), [mod-content-views](mod-content-views.md),
[mod-ui-identity](mod-ui-identity.md), [mod-eco](mod-eco.md).

Sandboxing - bubblewrap, one preset file per piece of software, `global.toml` as
the implicit system preset, protected paths (`/`, `$HOME`, daemon config, preset
directory, session state root), private state, project directories always
read-write, the `escapes` warning. The README's backup warning belongs here in
full. Sources: [sandbox-isolation](sandbox-isolation.md),
[daemon-presets](daemon-presets.md), [agent-grants](agent-grants.md).

Integrations - which agents work and how a preset adds one, quota polling
(Anthropic credentials re-read per poll and never copied, OpenRouter), `slopctl`
and task mailboxes, shortcuts and errands, the git view, attaching to tmux from
the host, the systemd user service. Sources: [daemon-usage](daemon-usage.md),
[agent-tasks](agent-tasks.md), [daemon-shortcuts](daemon-shortcuts.md),
[mod-ui-git](mod-ui-git.md), [paths](paths.md).

Fun - the jukebox and what it plays, the dead ground, what a working agent
builds, skyfallers, agent titles, the baked menu background. Heaviest screenshot
density, no obligations. Sources: [mod-jukebox](mod-jukebox.md),
[mod-jukebox-library](mod-jukebox-library.md),
[mod-plague](mod-plague.md), [mod-worksite](mod-worksite.md),
[skyfallers](skyfallers.md), [agent-titles](agent-titles.md),
[mod-background](mod-background.md).

FAQ sections - before you start; setup and troubleshooting; sandboxing and
safety; agents and sessions; interface; integrations; performance; fun and lore;
bugs and contributing. Troubleshooting stays a section here rather than becoming
its own page. First seeds from [gotchas](gotchas.md): 1.6 only; launching
`RimWorldLinux` directly bypasses the profile ([profile](profile.md)); Harmony
failures surface in `Player.log` at runtime as `patching incomplete:`.

## Open decisions

- README currently carries the install steps. Two copies drift; make the book
  canonical and cut README to pitch, warning, quickstart, and links.
- Images can be added later under `docs/src/images/`; none are required for the
  first revision.
- `docs/book/` is committed output: rebuild before committing `src/` changes, or
  the published book lags.
