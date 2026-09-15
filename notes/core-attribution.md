# Attribution and notices

## Prominent credits

- RimWorld / Ludeon Studios: host game and mod API; Unity: runtime and UI/rendering.
- Harmony, Andreas Pardeike and contributors: runtime patching. The bundled
  `0Harmony.dll` is assembly version 2.4.1.0 from Harmony RimWorld mod v2.4.2.0.
- Markdig 0.18.3, Alexandre Mutel: native Markdown parsing in the mod, BSD-2-Clause;
  the shipped notice is beside `Markdig.dll`.
- Tomlyn 0.19.0, Alexandre Mutel: TOML parsing and scalar string authoring in the mod,
  BSD-2-Clause; the shipped notice is beside `Tomlyn.dll`.
- Rust; Alacritty (`alacritty_terminal`): daemon and terminal emulator core.
- Tokio and Axum: async runtime and HTTP/WebSocket server.
- RustAudio's Rodio/CPAL and Symphonia: playback and audio decoding.
- tmux, bubblewrap and systemd: external session, isolation and service foundations;
  acknowledge them, but they are system dependencies rather than bundled code.

## Shipped art and sound

- Codicons, Microsoft/VS Code: the baked action icons.
- Nerd Fonts: supplied the Codicons-patched build font. The font is not shipped, but
  the derived PNG glyphs are.
- Material Icon Theme, Material Extensions: the vendored file-icon SVGs, MIT,
  copyright 2025 Material Extensions. The notice is `tools/fileicons/LICENSE`.
- Noto Color Emoji, Google: the rasterized radio and wilted-rose glyph artwork.
- Terry Fail: `pace`, `dive`, `hime` and `dawn` soundtrack export.
- User-provided jukebox stations are not shipped or named by SlopWorld.

The procedural robot faceplate is SlopWorld's own art.

## Notices and integrations

The direct crates worth naming on the human page are Alacritty Terminal; Tokio, Axum,
Tower HTTP and Tracing; Rodio, CPAL and Symphonia; Serde; ureq/rustls; anyhow, futures,
nix, regex, dirs and toml. The transitive graph includes Apache, MIT, MPL, BSD, ISC,
Unicode and CDLA terms, so the curated page is not a replacement for
`THIRD_PARTY_LICENSES`.

The generated mdBook distributes Open Sans (Apache-2.0) and Source Code Pro / Adobe
(OFL-1.1); its copies of both licences live in `docs/book/fonts/`.

Anthropic/Claude Code, OpenAI/Codex, OpenRouter, OpenCode and Ollama belong under
"Works with", not among SlopWorld's authors.
