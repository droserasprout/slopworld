# Original palette references

Non-SlopWorld schemes must preserve upstream colors, roles and
opacity even if our contrast checks dislike them. SlopWorld Warm/Cold/Calm are house
schemes. This is reference data, not a claim that the current implementation matches.

Hex values below are opaque unless an alpha is explicitly present. ANSI rows run
from slot 0 through 15. A palette is not necessarily a complete widget theme: do not
present a locally invented role mapping or a third-party port as the original.
Links to upstream branches are live references, not immutable snapshots.

## Dracula Classic

Source: [official specification](https://draculatheme.com/spec), including its
[ANSI/UI tables](https://github.com/dracula/draculatheme.com/blob/main/content/spec.mdx).

| Role | Hex |
| --- | --- |
| Background | `#282a36` |
| Foreground | `#f8f8f2` |
| Comment / current-line token | `#6272a4` |
| Selection | `#44475a` |
| Red | `#ff5555` |
| Orange | `#ffb86c` |
| Yellow | `#f1fa8c` |
| Green | `#50fa7b` |
| Cyan | `#8be9fd` |
| Purple | `#bd93f9` |
| Pink | `#ff79c6` |
| Floating UI / background light | `#343746` |
| Background lighter | `#424450` |
| Background dark / darker | `#21222c` / `#191a21` |

Current-line token can be translucent; the specification supplies `#353747` as an
opaque fallback. That does not make selection a generic accent wash.

```text
ANSI 0–7:  #21222c #ff5555 #50fa7b #f1fa8c #bd93f9 #ff79c6 #8be9fd #f8f8f2
ANSI 8–15: #6272a4 #ff6e6e #69ff94 #ffffa5 #d6acff #ff92df #a4ffff #ffffff
```

## Solarized Dark and Light

Source: [author's definitions and role guidance](https://github.com/altercation/solarized/blob/master/README.md).

| Token | Hex | Token | Hex |
| --- | --- | --- | --- |
| base03 | `#002b36` | yellow | `#b58900` |
| base02 | `#073642` | orange | `#cb4b16` |
| base01 | `#586e75` | red | `#dc322f` |
| base00 | `#657b83` | magenta | `#d33682` |
| base0 | `#839496` | violet | `#6c71c4` |
| base1 | `#93a1a1` | blue | `#268bd2` |
| base2 | `#eee8d5` | cyan | `#2aa198` |
| base3 | `#fdf6e3` | green | `#859900` |

| Role | Dark | Light |
| --- | --- | --- |
| Background | base03 | base3 |
| Background highlight | base02 | base2 |
| Body text | base0 | base00 |
| Emphasized text | base1 | base01 |
| Subdued text | base01 | base1 |

Dark body text is `#839496`, Light body text is `#657b83`. These are not interchangeable
with emphasized text. The standard terminal slot mapping is also in
[GNOME Terminal's Solarized table](https://github.com/GNOME/gnome-terminal/blob/gnome-46/src/profile-editor.cc):

```text
ANSI 0–7:  #073642 #dc322f #859900 #b58900 #268bd2 #d33682 #2aa198 #eee8d5
ANSI 8–15: #002b36 #cb4b16 #586e75 #657b83 #839496 #6c71c4 #93a1a1 #fdf6e3
```

Changing Light mode's ANSI slots is a separate port decision, not implied by swapping
the default foreground/background.

## Nord

Source: [official palette and role guidance](https://www.nordtheme.com/docs/colors-and-palettes/).

```text
nord0–3:   #2e3440 #3b4252 #434c5e #4c566a
nord4–6:   #d8dee9 #e5e9f0 #eceff4
nord7–10:  #8fbcbb #88c0d0 #81a1c1 #5e81ac
nord11–15: #bf616a #d08770 #ebcb8b #a3be8c #b48ead
```

Dark background uses nord0; elevated controls use nord1; nord2 supports selection;
nord3 supports guides/comments. nord8 is the primary accent. Upstream role guidance
is explicitly flexible; preserve the palette without claiming one universal widget map.

[Official GNOME Terminal port](https://github.com/nordtheme/gnome-terminal/blob/develop/src/nord.sh):

```text
background #2e3440; foreground #d8dee9
cursor background #d8dee9; cursor text #3b4252
selection background #88c0d0; selection text #2e3440
ANSI 0–7:  #3b4252 #bf616a #a3be8c #ebcb8b #81a1c1 #b48ead #88c0d0 #e5e9f0
ANSI 8–15: #4c566a #bf616a #a3be8c #ebcb8b #81a1c1 #b48ead #8fbcbb #eceff4
```

## Gruvbox Dark

Source: [original Vim theme](https://github.com/morhetz/gruvbox/blob/master/colors/gruvbox.vim).

```text
dark0 hard/normal/soft: #1d2021 #282828 #32302f
dark1–4:               #3c3836 #504945 #665c54 #7c6f64
gray:                  #928374
light0 hard/normal/soft:#f9f5d7 #fbf1c7 #f2e5bc
light1–4:              #ebdbb2 #d5c4a1 #bdae93 #a89984
bright red/green/yellow/blue/purple/aqua/orange:
                       #fb4934 #b8bb26 #fabd2f #83a598 #d3869b #8ec07c #fe8019
neutral red/green/yellow/blue/purple/aqua/orange:
                       #cc241d #98971a #d79921 #458588 #b16286 #689d6a #d65d0e
```

Default dark background is `#282828`, body text `#ebdbb2`, cursor-line background
`#3c3836`, and Visual background token `#665c54`. Preserve upstream's configurable
inverse-selection behavior when comparing complete renderings.

```text
ANSI 0–7:  #282828 #cc241d #98971a #d79921 #458588 #b16286 #689d6a #a89984
ANSI 8–15: #928374 #fb4934 #b8bb26 #fabd2f #83a598 #d3869b #8ec07c #ebdbb2
```

## One Dark

Sources: Atom's original [syntax colors](https://github.com/atom/one-dark-syntax/blob/master/styles/colors.less),
[UI roles](https://github.com/atom/one-dark-ui/blob/master/styles/ui-variables.less),
and [dynamic UI color formulas](https://github.com/atom/one-dark-ui/blob/master/styles/ui-variables-custom.less).
The UI derives colors from the syntax background; it is not a fixed syntax-palette copy.

Original syntax values (retain HSL to avoid rounding ambiguity):

| Role | HSL |
| --- | --- |
| Background | `hsl(220, 13%, 18%)` |
| Default text | `hsl(220, 14%, 71%)` |
| Secondary / muted | `hsl(220, 9%, 55%)` / `hsl(220, 10%, 40%)` |
| Cyan | `hsl(187, 47%, 55%)` |
| Blue | `hsl(207, 82%, 66%)` |
| Purple | `hsl(286, 60%, 67%)` |
| Green | `hsl(95, 38%, 62%)` |
| Red / dark red | `hsl(355, 65%, 65%)` / `hsl(5, 48%, 51%)` |
| Orange / yellow | `hsl(29, 54%, 61%)` / `hsl(39, 67%, 69%)` |
| Syntax accent | `hsl(220, 100%, 66%)` |

Original UI subtle text removes 40% opacity from UI text; selected text is white.
Input background darkens base by 6%; selection lightens base by 8%. These are
upstream formulas, unlike SlopWorld's generic adapter. Atom's syntax source does
not define a canonical 16-slot terminal palette.

## GNOME Dark/Light and Tango Dark/Light

Source: [GNOME Terminal 46 built-in schemes and ANSI palettes](https://github.com/GNOME/gnome-terminal/blob/gnome-46/src/profile-editor.cc).
These are terminal presets, not complete Adwaita widget themes.

| Preset | Foreground | Background |
| --- | --- | --- |
| GNOME Dark | `#d0cfcc` | `#171421` |
| GNOME Light | `#171421` | `#ffffff` |
| Tango Dark | `#d3d7cf` | `#2e3436` |
| Tango Light | `#2e3436` | `#eeeeec` |

```text
GNOME 0–7:  #171421 #c01c28 #26a269 #a2734c #12488b #a347ba #2aa1b3 #d0cfcc
GNOME 8–15: #5e5c64 #f66151 #33d17a #e9ad0c #2a7bde #c061cb #33c7de #ffffff
Tango 0–7:  #2e3436 #cc0000 #4e9a06 #c4a000 #3465a4 #75507b #06989a #d3d7cf
Tango 8–15: #555753 #ef2929 #8ae234 #fce94f #729fcf #ad7fa8 #34e2e2 #eeeeec
```

The [GNOME HIG palette](https://developer.gnome.org/hig/reference/palette.html) is
explicitly for icons/illustrations. Its Yellow 5 is `#e5a50a`; preserve that original
hue when using that palette. Do not conflate HIG colors, terminal presets and Adwaita.

## Monokai: original provenance still unresolved

[Author's history](https://monokai.pro/history) distinguishes original Monokai from
Monokai Pro. No definitive original complete UI/ANSI table was verified in this audit.

[Microsoft's Monokai port](https://github.com/microsoft/vscode/blob/main/extensions/theme-monokai/themes/monokai-color-theme.json)
identifies these original-derived colors; this is a port reference, not certification:

```text
editor background #272822; editor foreground #f8f8f2
tab well / borders #1e1f1c; selection token #414339; focus token #75715e
```

That port currently uses `#878b9180` for editor selection, `#75715e` for active list
selection/button background, and `#3e3d32` for list hover. Preserve the distinction
between historical palette tokens and current port roles. Do not label our existing
16-color ANSI adaptation as an authenticated original.

## VS Code Dark+

Sources: [Dark+](https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_plus.json)
includes [Dark (Visual Studio)](https://github.com/microsoft/vscode/blob/main/extensions/theme-defaults/themes/dark_vs.json).
Unspecified values inherit application color-registry defaults; the theme JSON alone
is not the complete palette.

| Explicit role | Hex |
| --- | --- |
| Editor background / foreground | `#1e1e1e` / `#d4d4d4` |
| Inactive editor selection | `#3a3d41` |
| Menu background / foreground | `#252526` / `#cccccc` |
| Menu border / separator | `#454545` |
| Menu selection background | `#0078d4` |
| Sidebar title foreground | `#bbbbbb` |
| Input placeholder foreground | `#a6a6a6` |
| Activity badge background | `#007acc` |
| Checkbox border | `#6b6b6b` |

The [terminal registry](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/terminal/common/terminalColorRegistry.ts)
defines dark terminal foreground as `#cccccc` and inherits terminal selection from
editor selection. Do not substitute editor foreground for terminal foreground merely
because both belong to Dark+. Pin an upstream release before implementing exact fidelity.

## Local follow-up

Shipped role values now live in the [theme catalogs](mod-ui-identity.md).
`UIScheme.Sel` remains 35% accent, and `TerminalTheme` multiplies selection alpha by
0.35. These are local policies, not original palette definitions. Missing upstream
roles need an explicit documented mapping; imported colors should not be adjusted
to satisfy local contrast checks.
