# Theme identity and upstream references

`UIScheme` owns chrome roles; `TerminalTheme` owns ANSI and pane colors. Persist stable
IDs, not picker labels. Match UI follows the UI scheme's same-ID terminal theme;
explicit terminal choices remain independent. Each shipped UI scheme needs a matching
terminal theme; terminal-only IDs are allowed.

`ThemeCatalog` loads one definition per file under `mod/Themes/UI/` and
`mod/Themes/Terminal/`. `ThemeLoader` shares loading/error recovery; each consumer
owns its cache and fallback. Derived roles stay out of editable catalogs. Accent,
destructive, and checkbox faces must be opaque for contrast-derived text.
Validate with `just mod-check-themes`.

Imported schemes preserve upstream colors, roles, and opacity even when contrast
checks fail. SlopWorld Warm/Cold/Calm are house schemes. A palette is not a complete
widget theme, and local ANSI/role adaptations must not be presented as originals.
Use these sources rather than keeping a second set of color tables in notes:

| Scheme | Reference and caveat |
| --- | --- |
| Dracula | [Specification](https://draculatheme.com/spec); selection and current-line roles differ. |
| Solarized | [Author's roles](https://github.com/altercation/solarized/blob/master/README.md) and [terminal mapping](https://github.com/GNOME/gnome-terminal/blob/gnome-46/src/profile-editor.cc); body and emphasized text differ, and Light does not imply remapped ANSI slots. |
| Nord | [Palette guidance](https://www.nordtheme.com/docs/colors-and-palettes/) and [terminal port](https://github.com/nordtheme/gnome-terminal/blob/develop/src/nord.sh); upstream permits flexible widget roles. |
| Gruvbox | [Original Vim theme](https://github.com/morhetz/gruvbox/blob/master/colors/gruvbox.vim); account for contrast variants and inverse selection. |
| One Dark | Atom's [syntax](https://github.com/atom/one-dark-syntax/blob/master/styles/colors.less), [UI roles](https://github.com/atom/one-dark-ui/blob/master/styles/ui-variables.less), and [formulas](https://github.com/atom/one-dark-ui/blob/master/styles/ui-variables-custom.less); syntax alone defines neither the UI nor a canonical ANSI palette. |
| GNOME / Tango | [Terminal 46 presets](https://github.com/GNOME/gnome-terminal/blob/gnome-46/src/profile-editor.cc); these are distinct from Adwaita and the [HIG illustration palette](https://developer.gnome.org/hig/reference/palette.html). |
| Monokai | [Microsoft's port](https://github.com/microsoft/vscode/blob/main/extensions/theme-monokai/themes/monokai-color-theme.json); a port reference, not an authenticated original ANSI palette. |
| VS Code Dark+ | [Dark+](https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_plus.json), inherited [Dark](https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_vs.json), and [terminal registry](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/terminal/common/terminalColorRegistry.ts); unspecified roles inherit registry defaults, and terminal/editor foregrounds differ. |

Branch links can change; pin an upstream release before implementing exact fidelity.
[Chrome](mod-ui-chrome.md) owns shared metrics; [terminal rendering](mod-terminal-rendering.md)
owns texture invalidation.
