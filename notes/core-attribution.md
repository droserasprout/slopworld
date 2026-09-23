# Attribution and notices

## Prominent credits

The About page groups libraries by daemon and client ownership, with shared foundations
under "Built with" and visual sources under "Assets".

- RimWorld / Ludeon Studios: host game and mod API. Unity: runtime and UI/rendering.
- Harmony, Andreas Pardeike and contributors: runtime patching. The bundled
  `0Harmony.dll` is assembly version 2.4.1.0 from Harmony RimWorld mod v2.4.2.0.
- Markdig 0.18.3, Alexandre Mutel: native Markdown parsing in the mod, BSD-2-Clause.
  The supplied notice is beside `Markdig.dll`.
- Tomlyn 0.19.0, Alexandre Mutel: TOML parsing and scalar string authoring in the mod,
  BSD-2-Clause. The supplied notice is beside `Tomlyn.dll`.
- Protocol Buffers: prost in the daemon and Google.Protobuf in the client.
- Newtonsoft.Json, James Newton-King: client JSON parsing for SongRec results.
- SongRec: external song-identification integration called by the client.
- Rust and Alacritty (`alacritty_terminal`): daemon and terminal emulator core.
- Tokio and Axum: async runtime and HTTP/WebSocket server.
- RustAudio's Rodio/CPAL and Symphonia: playback and audio decoding.
- tmux, bubblewrap and systemd: external session, isolation, and service foundations.
  Acknowledge them as system dependencies rather than bundled code.
- Landlock: daemon crate for filesystem write restrictions during worktree allocation.

## Shipped art and sound

- Codicons, Microsoft/VS Code: the baked action icons.
- Nerd Fonts: supplied the Codicons-patched build font. The font is not shipped, but
  the derived PNG glyphs are.
- Material Icon Theme, Material Extensions: the vendored file-icon SVGs, MIT,
  copyright 2025 Material Extensions. The notice is `tools/fileicons/LICENSE`.
- Noto Color Emoji, Google: the rasterized radio and wilted-rose glyph artwork.
- Classic Console Neue, DeeJayy: bundled loading-screen font (`assets/fonts/clacon2.ttf`).
- Terry Fail: `pace`, `dive`, `hime` and `dawn` soundtrack export.
- User-provided jukebox stations are not shipped or named by SlopWorld.

The procedural robot faceplate is SlopWorld's own art.

## Notices and integrations

Name these direct crates on the human page:

- Alacritty Terminal.
- Tokio, Axum, Tower HTTP, and Tracing.
- Rodio, CPAL, and Symphonia.
- Serde and ureq/rustls.
- anyhow, futures, nix, regex, dirs, landlock, and toml.
 The transitive graph includes Apache, MIT, MPL, BSD, ISC,
Unicode and CDLA terms, so the curated page is not a replacement for
`THIRD_PARTY_LICENSES`.

The generated mdBook distributes Open Sans (Apache-2.0) and Source Code Pro / Adobe
(OFL-1.1). Copies of both licences are in `docs/book/fonts/`.

Anthropic/Claude Code, OpenAI/Codex, OpenRouter, OpenCode and Ollama belong under
"Works with", not among SlopWorld's authors.
