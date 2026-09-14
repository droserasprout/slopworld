# Experimental-feature gaps

Breadcrumbs/Instructions controls and runtime delivery disagree. Fix these boundaries:

- `manager/capture_input.rs::paste_breadcrumb` must reject manual delivery when Breadcrumbs
  is disabled before queuing paste.
- Pending entries need provenance. Disabling Instructions, global discovery or per-agent
  discovery must remove only ineligible discovery; disabling Breadcrumbs cancels everything.
  Recheck at consumption. Re-enabling cannot resurrect cancelled/consumed text; existing
  mounts remain until restart and saved preferences survive gating.
- `handlers_config.rs::instructions_preview` treats empty project as the first configured
  project. Give the UI sample selection an explicit synthetic meaning independent of live
  configuration; retain named-project errors and unsaved template/path preview.
- Worker bootstrap is unconditional. Unlock `WorkersPage` editing without gating task delivery.
  Explain effective discovery prerequisites in the agent editor and `BreadcrumbList`; link
  to Settings > Agents > Instructions, with feature switches under General > Experimental.

Add regression cases before fixes: all switch combinations, queued ordinary/discovery entries,
per-agent mount opt-out, re-enable behavior, and zero/one/many-project previews. Use a small
production availability helper if needed for game-free UI tests, not source-text assertions.
Run daemon/mod tests and mod lint through make. Update [Settings boundaries](ui-settings.md)
and protocol notes if the request shape changes.
