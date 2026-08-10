# Attribution

The eventual attribution page should be a readable set of prominent credits plus a
link to complete release-specific third-party notices. Do not turn the page itself
into the whole Cargo dependency graph.

## Prominent credits

- RimWorld / Ludeon Studios: host game and mod API; Unity: runtime and UI/rendering.
- Harmony, Andreas Pardeike and contributors: runtime patching. The bundled
  `0Harmony.dll` is 2.3.3.
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
- Terry Fail: `slopbg01` and `slopbg02` soundtrack.
- Radio Paradise, WEFUNK Radio and WALM / Classic Vinyl HD: jukebox stream sources.

The procedural robot faceplate is SlopWorld's own art.

## Notices and integrations

Generate complete Rust notices from the locked release graph. The direct crates worth
naming on the human page are Alacritty Terminal; Tokio, Axum, Tower HTTP and Tracing;
Rodio, CPAL and Symphonia; Serde; ureq/rustls; anyhow, futures, nix, regex, dirs and
toml. The transitive graph includes Apache, MIT, MPL, BSD, ISC, Unicode and CDLA terms,
so the curated page is not a replacement for `THIRD_PARTY_LICENSES`.

The generated mdBook distributes Open Sans (Apache-2.0) and Source Code Pro / Adobe
(OFL-1.1); its copies of both licences live in `docs/book/fonts/`.

Anthropic/Claude Code, OpenAI/Codex, OpenRouter, OpenCode and Ollama belong under
"Works with", not among SlopWorld's authors. Include a neutral statement that
SlopWorld is independent and is not affiliated with or endorsed by the named games,
vendors, projects or services.

## Before publishing

- Ship Harmony's full copyright and licence notice beside its DLL.
- Record and ship the exact Codicons licence/source revision.
- Record the exact Nerd Font build input and its applicable licence bundle.
- Ship the Noto Color Emoji copyright and licence notice for the derived artwork.
- Record Terry Fail's copyright and the distribution licence for both OST tracks.
- Generate Rust notices from `Cargo.lock` and preserve the Material Icon Theme notice
  in release packages, not only in the source tree.

Also fix the README's `alactitty` typo to `Alacritty` when that prose is next touched.
