# File icons

`svg/` is a subset of the [Material Icon Theme](https://github.com/material-extensions/vscode-material-icon-theme)
for VS Code, vendored under its MIT licence (`LICENSE`). `manifest.toml` says which
of them the files view draws and what earns each one; `../fileicons.py` bakes them
into `mod/Textures/SlopWorld/FileIcons/`.

Vendored rather than fetched at build time so a bake is offline and the same on every
machine. `python3 tools/fileicons.py --fetch` refreshes the SVGs from upstream, which
is also when this licence wants checking.

There is no open-folder icon: upstream generates that variant at runtime and ships
only `folder-base`, and the tree's chevron already says which way a directory is
facing.
