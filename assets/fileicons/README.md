# File icons

`svg/` contains files from the [Material Icon Theme](https://github.com/material-extensions/vscode-material-icon-theme)
for VS Code. The project vendors them under the MIT license (`LICENSE`).
`manifest.toml` lists which files the file view uses and the rules for each file.
`../../tools/assets/fileicons.py` builds the icons into `mod/Textures/SlopWorld/FileIcons/`.

The project vendors the SVG files so builds work offline and use the same assets on every
machine. `python3 tools/assets/fileicons.py --fetch` downloads updates from upstream.
Check the license when you fetch updates.

The upstream project creates the open-folder variant at runtime and ships only
`folder-base`. The tree chevron shows whether a directory is open or closed.
