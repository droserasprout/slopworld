# Developer tools

Use Makefile targets; [build commands](build-commands.md) and the
[build guide](../docs/src/build.md) own workflows. `tools/` is the script inventory.

Committed icon/emoji assets keep ordinary builds independent of local bake fonts.
Action icons, file icons and terminal emoji have different manifests/loaders; see
[icons](mod-icons.md) before rebaking. OST staging and installation are separate steps;
installation updates both audio files and SongDefs.

Prose lint warnings are review prompts, not factual-quality or authorship judgments.
Attribution enforcement is separate. Run targeted lint through `make lint-prose
PROSE_LINT_ARGS=...`; missing explicit inputs are errors. Do not launch the game or use
capture/image tools unless requested.
