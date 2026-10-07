# Third-party notices

This directory is the canonical source for bundled third-party license texts and
attribution. `just refresh-licenses` copies it into `mod/About/ThirdPartyNotices/`
and copies the root project `LICENSE` into `mod/About/LICENSE`. Release archives,
Arch packages, and the sidecar image also carry readable copies. These notices
cover the bundled assets and mod runtime libraries; they are not a complete
inventory of software installed in the sidecar. The generated
[Rust dependency table](rust-dependencies.md) lists crate versions and license
metadata; it does not include their full license texts or notices.

| Component | License | Full text | Attribution and use |
| --- | --- | --- | --- |
| [Classic Console Neue](https://webdraft.hu/fonts/classic-console/) | MIT | [License](classic-console-neue/LICENSE.txt) | DeeJayy, 2011–2025; printable ASCII glyphs rendered into the loading-screen atlas. |
| [Noto Emoji](https://github.com/googlefonts/noto-emoji) | SIL OFL 1.1 | [License](noto-emoji/LICENSE.txt) | Google; glyphs rendered into emoji atlases and application icons. |
| [Material Icon Theme](https://github.com/material-extensions/vscode-material-icon-theme) | MIT | [License](material-icon-theme/LICENSE.txt) | Material Extensions; selected SVGs rendered into file icons. |
| [Symbols Nerd Font 3.5.1](https://github.com/ryanoasis/nerd-fonts) | MIT | [License](nerd-fonts/LICENSE) | Ryan L McIntyre and contributors; build input for action icons. See the [upstream notices](nerd-fonts/UPSTREAM-NOTICES.txt) for glyph sets. |
| [Codicons](https://github.com/microsoft/vscode-codicons) | CC BY 4.0 | [License](nerd-fonts/licenses/codicons/LICENSE.txt) | Microsoft; action glyphs rendered, scaled, and tinted by SlopWorld. |
| Font Awesome glyphs | CC BY 4.0 | [License](nerd-fonts/licenses/font-awesome/LICENSE.txt) | Upstream glyph license preserved with Symbols Nerd Font. |
| Material Design glyphs | Apache 2.0 | [License](nerd-fonts/licenses/materialdesign/Apache-2.0.txt) | See the [Pictogrammers notice](nerd-fonts/licenses/materialdesign/LICENSE). |
| Octicons glyphs | MIT | [License](nerd-fonts/licenses/octicons/LICENSE) | Upstream glyph license preserved with Symbols Nerd Font. |
| Pomicons glyphs | SIL OFL 1.1 | [License](nerd-fonts/licenses/pomicons/LICENSE) | Reserved Font Name: Pomicons. |
| Powerline Extra glyphs | MIT | [License](nerd-fonts/licenses/powerline-extra/LICENSE) | Upstream glyph license preserved with Symbols Nerd Font. |
| Powerline Symbols glyphs | MIT | [License](nerd-fonts/licenses/powerline-symbols/LICENSE.txt) | Upstream glyph license preserved with Symbols Nerd Font. |
| [Unicode emoji data 17.0](https://www.unicode.org/emoji/17.0/) | Unicode License v3 | [License](unicode/LICENSE.txt) | Unicode, Inc.; emoji sequence catalog generated from `emoji-test.txt`. |
| [Harmony](https://github.com/pardeike/HarmonyRimWorld) | MIT | [License](runtime/0Harmony.LICENSE.txt) | Andreas Pardeike; mod runtime library. |
| Google.Protobuf | BSD 3-Clause | [License](runtime/Google.Protobuf.LICENSE.txt) | Mod runtime library. |
| AngleSharp | MIT | [License](runtime/AngleSharp.LICENSE.txt) | Mod HTML parser. |
| Markdig | BSD 2-Clause | [License](runtime/Markdig.LICENSE.txt) | Mod runtime library. |
| Newtonsoft.Json | MIT | [License](runtime/Newtonsoft.Json.LICENSE.txt) | Mod runtime library. |
| Tomlyn | BSD 2-Clause | [License](runtime/Tomlyn.LICENSE.txt) | Mod runtime library. |
| System.Text.Encoding.CodePages | MIT | [License](runtime/System.Text.Encoding.CodePages.LICENSE.txt) | Mod runtime library; includes [third-party notices](runtime/System.Text.Encoding.CodePages.NOTICES.txt). |
| System.Buffers | MIT | [License](runtime/System.Buffers.LICENSE.txt) | Mod runtime library. |
| System.Memory | MIT | [License](runtime/System.Memory.LICENSE.txt) | Mod runtime library; includes additional [third-party notices](runtime/System.Memory.NOTICES.txt). |
| System.Numerics.Vectors | MIT | [License](runtime/System.Numerics.Vectors.LICENSE.txt) | Mod runtime library. |
| System.Runtime.CompilerServices.Unsafe | MIT | [License](runtime/System.Runtime.CompilerServices.Unsafe.LICENSE.txt) | Mod runtime library. |

The bundled `clacon2.ttf` identifies its license as MIT and carries the copyright
notice reproduced above. The glyph notices describe the bundled Symbols Nerd
Font; SlopWorld's action icons use Codicons.

SlopWorld's own MIT license stays in the repository root as `LICENSE` and ships
with the mod as `About/LICENSE`. Generated copies must not be edited.
