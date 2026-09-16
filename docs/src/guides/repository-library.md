# Repository Library

A registered project can provide every Library item kind from its checkout:

```text
.slopworld/
  library/
    review.toml
    tests.toml
    rules.toml
    size.toml
  templates/
    reviewer.toml
```

Each TOML file defines one item. The daemon reads these directories under the registered
project directory, including when running in a sidecar. Names appear as `project::name`,
so two repositories can both provide `review`. Personal entries keep their existing names.
Use the qualified name in breadcrumb references and template API requests.

Open Library or press its refresh button to see file edits and removals. API catalog reads
and entry lookups also read current files. Repository entries show their source and are
read-only in the UI. Edit the file, or duplicate the entry into the personal catalog.
Discovery never runs an item, starts an agent, or attaches a breadcrumb automatically.

## Prompts, shell errands, breadcrumbs, and file actions

`.slopworld/library/review.toml`:

```toml
name = "review"
kind = "prompt"
text = "Review the current diff and identify correctness problems."
```

`.slopworld/library/tests.toml`:

```toml
name = "tests"
kind = "shell"
text = "make test"
```

`.slopworld/library/rules.toml`:

```toml
name = "rules"
kind = "breadcrumb"
text = "Read AGENTS.md before editing."
```

`.slopworld/library/size.toml`:

```toml
name = "size"
kind = "fa"
command = "du -sh"
mode = "open_terminal"
```

These use the same fields as personal `[[library]]` records. The owning project supplies
`project`; the file cannot impersonate another project or a builtin. Prompts and shell
errands may set `link = "ask"` or `link = "temp"` for the usual destination choices.
Breadcrumbs must be selected explicitly in the project or agent editor. File actions
appear in the owning project's file-action menus. Text lives inline in the TOML file;
multiline TOML strings work for longer prompts.

## Agent templates

`.slopworld/templates/reviewer.toml`:

```toml
name = "reviewer"
description = "Review a checkout using Codex"

[defaults]
cmd = "codex"
network = "private"

[[defaults.prompts]]
name = "review-instructions"
text = "Review the current diff and report correctness problems."
```

A template uses the same portable `defaults` as the template API: an optional complete
`command` preset, a raw `cmd`, sandbox names and their `sandbox_presets` definitions,
prompt snapshots, network, DNS, limits, and startup flags. Preset definitions must be
self-contained; unresolved dependencies are rejected. All scalar choices are optional.
Omitted network, DNS and limit fields inherit from the destination project; an omitted command
uses the destination daemon's default agent. Omitted startup flags use session defaults
(currently false except `instructions_breadcrumb` and `breadcrumb_yolo`, which are true).
Explicit values, including false, are copied as choices. A recipe may have an empty
`[defaults]` table. See [inheritance and overrides](configuring-agents.md#project-defaults-and-agent-overrides).

The daemon supplies origin metadata. Repository templates do not need a `version`.
Creating an agent or duplicating into the personal catalog copies the definition and its
snapshots; later repository changes do not rewrite those copies. A template does not
capture mounts, credentials, or agent identity. Existing launch and sandbox validation
still applies when creating or starting an agent.

Invalid definitions are skipped with a daemon log diagnostic; other valid files remain
available. Duplicate names within one project and catalog use the first valid file in
filename order, with a diagnostic for the duplicate. Files are limited to 1 MiB and
symlinks must resolve inside the registered checkout.
