# Developer tools

Use Makefile targets.
[Build commands](build-commands.md) and the [build guide](../docs/src/build.md) describe workflows. `tools/` is the script inventory.

Committed icon/emoji assets keep ordinary builds independent of local bake fonts.
`python3 tools/emoji_atlas.py` generates the shared text sprite texture and `UI/Text/TextSpriteData.cs`
together. Optional `--sequences /path/to/keys.txt` adds literal UTF-8
text keys, one per line.
`make bake-loading-font` generates the loading screen's ASCII PNG atlas from
`assets/fonts/clacon2.ttf`; it is a build-time input, not an installed runtime font.
At runtime, the renderer matches the longest generated key. Metadata order is atlas slot
order, so never sort or extend the generated keys without rebaking their artwork.
Action icons, file icons, and terminal emoji have different manifests and loaders.
See [icons](mod-icons.md) before generating them again.
OST staging and installation are separate steps.
Installation updates both audio files and SongDefs.

Do not launch the game or use capture/image tools unless requested.
