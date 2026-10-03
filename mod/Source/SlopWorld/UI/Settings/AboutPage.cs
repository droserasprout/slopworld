using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public sealed class AboutPage : IOptionPage
    {
        const GameFont RegularFont = GameFont.Medium;
        const float ContentPaddingX = 20f;
        const float ContentPaddingY = 16f;
        static float HeroMargin => UiTheme.GapS;
        const float HeroIconSize = 88f;
        static float HeroIconGap => UiTheme.GapXS;
        static float HeroTitleGap => UiTheme.GapL;
        static float HeadingGap => UiTheme.GapM;
        static float GroupGap => UiTheme.GapS;
        static float RowGap => UiTheme.GapXS;
        static float ColumnGap => UiTheme.GapL;
        static float TailPadding => UiTheme.GapL;
        const int TitleTextSize = 28;
        const int SectionTextSize = 14;
        const int SubheadingTextSize = 14;
        const int LeadTextSize = 22;
        const int MetaTextSize = 14;
        const int BodyTextSize = 15;

        const string EulaDisclaimer =
            "Portions of the materials used to create this content/mod are trademarks and/or " +
            "copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This " +
            "content/mod is not official and is not endorsed by Ludeon.";

        const float AutoScrollSpeed = 7f;
        const float FirstPassHeight = 2000f;

        public void Load() => _thanksText = "you";

        sealed class Credit
        {
            public readonly string Name;
            public readonly string Detail;
            public readonly CreditLink[] Links;

            public Credit(string name, string detail)
            {
                Name = name;
                Detail = detail;
                Links = Array.Empty<CreditLink>();
            }

            public Credit(string name, string detail, string url)
            {
                Name = name;
                Detail = detail;
                Links = string.IsNullOrEmpty(url)
                    ? Array.Empty<CreditLink>()
                    : new[] { new CreditLink(name, url) };
            }

            public Credit(string name, string detail, CreditLink[] links)
            {
                Name = name;
                Detail = detail;
                Links = links ?? Array.Empty<CreditLink>();
            }
        }

        sealed class CreditLink
        {
            public readonly string Label;
            public readonly string Url;

            public CreditLink(string label, string url)
            {
                Label = label;
                Url = url;
            }
        }

        static readonly Credit[] DaemonLibraries =
        {
            new Credit("Rust", "language", "https://www.rust-lang.org/"),
            new Credit("Tokio", "async runtime", "https://tokio.rs/"),
            new Credit("Alacritty", "terminal", "https://alacritty.org/"),
            new Credit("Rodio / CPAL / Symphonia", "audio", new[]
            {
                new CreditLink("Rodio", "https://github.com/RustAudio/rodio"),
                new CreditLink("CPAL", "https://github.com/RustAudio/cpal"),
                new CreditLink("Symphonia", "https://github.com/pdeljanov/Symphonia"),
            }),
            new Credit("ureq / rustls", "HTTP and TLS", new[]
            {
                new CreditLink("ureq", "https://github.com/algesten/ureq"),
                new CreditLink("rustls", "https://github.com/rustls/rustls"),
            }),
            new Credit("Axum", "HTTP/WebSockets", "https://github.com/tokio-rs/axum"),
            new Credit("Tower HTTP", "HTTP middleware", "https://github.com/tower-rs/tower-http"),
            new Credit("Serde / TOML / JSON", "serde and configs", new[]
            {
                new CreditLink("Serde", "https://serde.rs/"),
                new CreditLink("TOML", "https://github.com/toml-rs/toml"),
                new CreditLink("JSON", "https://github.com/serde-rs/json"),
            }),
            new Credit("prost", "Protocol Buffers",
                "https://github.com/tokio-rs/prost"),
            new Credit("Landlock", "filesystem isolation",
                "https://github.com/landlock-lsm/rust-landlock"),
            new Credit("Tracing", "diagnostics", "https://github.com/tokio-rs/tracing"),
            new Credit("anyhow / futures / nix / regex / dirs", "support", new[]
            {
                new CreditLink("anyhow", "https://github.com/dtolnay/anyhow"),
                new CreditLink("futures", "https://github.com/rust-lang/futures-rs"),
                new CreditLink("nix", "https://github.com/nix-rust/nix"),
                new CreditLink("regex", "https://github.com/rust-lang/regex"),
                new CreditLink("dirs", "https://github.com/dirs-dev/dirs-rs"),
            }),
        };

        static readonly Credit[] ClientLibraries =
        {
            new Credit("C#", "language",
                "https://learn.microsoft.com/dotnet/csharp/"),
            new Credit("Mono", "runtime", "https://www.mono-project.com/"),
            new Credit("Harmony", "RimWorld patching",
                "https://github.com/pardeike/HarmonyRimWorld"),
            new Credit("Markdig", "markdown",
                "https://github.com/xoofx/markdig"),
            new Credit("Tomlyn", "TOML configs",
                "https://github.com/xoofx/Tomlyn"),
            new Credit("Newtonsoft.Json", "JSON",
                "https://www.newtonsoft.com/json"),
            new Credit("Google.Protobuf", "Protocol Buffers",
                "https://github.com/protocolbuffers/protobuf"),
        };

        static readonly Credit[] Tools =
        {
            new Credit("tmux", "terminal sessions", "https://github.com/tmux/tmux/wiki"),
            new Credit("bubblewrap", "sandboxing",
                "https://github.com/containers/bubblewrap"),
            /* new Credit("systemd", "service manager", "https://systemd.io/"), */
            new Credit("passt", "networking", "https://passt.top/"),
            new Credit("less / bat", "pagers", new[]
            {
                new CreditLink("less", "https://www.greenwoodsoftware.com/less/"),
                new CreditLink("bat", "https://github.com/sharkdp/bat"),
            }),
            new Credit("highlight / Pygments", "syntax highlighters", new[]
            {
                new CreditLink("highlight", "https://gitlab.com/saalen/highlight"),
                new CreditLink("Pygments", "https://pygments.org/"),
            }),
            new Credit("Git", "version control", "https://git-scm.com/"),
            new Credit("SongRec", "song identification",
                "https://github.com/marin-m/SongRec"),
            new Credit("ncspot", "Spotify playback",
                "https://github.com/hrkfdn/ncspot"),
        };

        static readonly Credit[] Assets =
        {
            new Credit("Codicons", "icons",
                "https://github.com/microsoft/vscode-codicons"),
            new Credit("Nerd Fonts", "",
                "https://www.nerdfonts.com/"),
            new Credit("Material Icon Theme", "",
                "https://github.com/material-extensions/vscode-material-icon-theme"),
            new Credit("Classic Console Neue", "loading screen font",
                "https://webdraft.hu/fonts/classic-console/"),
            new Credit("Noto Color Emoji", "emojis",
                "https://github.com/googlefonts/noto-emoji"),
        };

        static readonly Credit Soundtrack = new Credit("Terry Fail", "Music By",
            "https://terryfail.bandcamp.com/");

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _contentHeight;
        readonly ContentHeight _height = new ContentHeight(FirstPassHeight);
        readonly AboutRobots _robots = new AboutRobots();
        int _autoScrollFrame = -1;
        string _thanksText = "you";

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.BodyWithoutFooter(rect);

            float viewWidth = Mathf.Max(1f, inner.width - UiTheme.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _height.BeginFrame(Time.frameCount));
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            using (_scroll.Scope(inner, view))
            {
                var content = new Rect(ContentPaddingX, ContentPaddingY,
                    Mathf.Max(1f, view.width - ContentPaddingX * 2f),
                    Mathf.Max(1f, view.height - ContentPaddingY * 2f));
                if (_robots.Visible)
                {
                    _contentHeight = _robots.Draw(content, inner.height - ContentPaddingY * 2f);
                    if (!_robots.Visible) _scroll.JumpTo(Vector2.zero);
                }
                else
                    _contentHeight = DrawCredits(content);
                _contentHeight += ContentPaddingY;
                _height.Measure(_contentHeight);
            }

            if (!_robots.Visible) AdvanceAutoScroll(inner, _contentHeight);
        }

        void AdvanceAutoScroll(Rect viewport, float contentHeight)
        {
            if (Event.current.type != EventType.Repaint || _autoScrollFrame == Time.frameCount)
                return;
            _autoScrollFrame = Time.frameCount;

            float max = Mathf.Max(0f, contentHeight - viewport.height);
            if (max <= 0f || _scroll.Position.y >= max) return;

            float next = Mathf.Min(max,
                _scroll.Position.y + AutoScrollSpeed * Time.unscaledDeltaTime);
            _scroll.JumpTo(new Vector2(_scroll.Position.x, next));
        }

        float DrawCredits(Rect r)
        {
            float y = r.y;

            y = DrawEulaDisclaimer(r, y);
            y = DrawHero(r, y);
            y = DrawMusic(r, y);
            y = DrawBasedOn(r, y);
            y = DrawLibraries(r, y);
            y = DrawTools(r, y);
            y = DrawAssets(r, y);
            y = DrawSpecialThanks(r, y);

            return y + TailPadding;
        }

        float DrawEulaDisclaimer(Rect r, float y)
        {
            y += HeroMargin;
            y = Paragraph(r, y, EulaDisclaimer, RegularFont, UiTheme.Dim,
                TextAnchor.UpperCenter, BodyTextSize);
            return y + HeroMargin;
        }

        float DrawHero(Rect r, float y)
        {
            y += HeroMargin;

            var icon = ContentFinder<Texture2D>.Get("SlopWorld/SlopWorld_icon", false);
            if (icon != null)
            {
                var iconRect = new Rect(r.center.x - HeroIconSize / 2f, y,
                    HeroIconSize, HeroIconSize);
                var wasColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = wasColor;
                if (_robots.HandleLogoClick(iconRect)) _scroll.JumpTo(Vector2.zero);
                y += HeroIconSize + HeroIconGap;
            }

            y = Link(r, y, "SlopWorld", "https://github.com/droserasprout/slopworld", GameFont.Medium,
                TextAnchor.UpperCenter, TitleTextSize);
            y += HeroTitleGap;
            y = ByLine(r, y, "CREATED BY", "Lev Gorodetskii",
                "https://drsr.io/projects");
            return y + HeroMargin;
        }

        float ByLine(Rect r, float y, string role, string name, string url,
            int roleTextSize = 0)
        {
            y = Line(r, y, role, GameFont.Small, UiTheme.Dim,
                TextAnchor.UpperCenter,
                roleTextSize > 0 ? roleTextSize : MetaTextSize);
            return Link(r, y, name, url, RegularFont,
                TextAnchor.UpperCenter, LeadTextSize);
        }

        float DrawBasedOn(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Built with") + HeadingGap;
            y = InlineLinkLine(r, y, "", "RimWorld by Ludeon Studios", "",
                "https://rimworldgame.com/", RegularFont);
            return InlineLinkLine(r, y, "", "Unity by Unity Technologies", "",
                "https://unity.com/", RegularFont);
        }

        float DrawMusic(Rect r, float y)
        {
            y = NextSection(y);
            y = ByLine(r, y, "MUSIC BY", Soundtrack.Name, Soundtrack.Links[0].Url);
            return y + GroupGap;
        }

        float DrawLibraries(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Daemon", SubheadingTextSize) + HeadingGap;
            y = CreditGrid(r, y, DaemonLibraries, 3);
            y = NextSection(y);
            y = SectionHeading(r, y, "Client", SubheadingTextSize) + HeadingGap;
            return CreditGrid(r, y, ClientLibraries, 2);
        }

        float DrawTools(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Tools") + HeadingGap;
            return CreditGrid(r, y, Tools, 3);
        }

        float DrawAssets(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Assets") + HeadingGap;
            return CreditGrid(r, y, Assets, 2);
        }

        float DrawSpecialThanks(Rect rect, float y)
        {
            y = NextSection(y);
            y = SectionHeading(rect, y, "Special thanks") + HeadingGap;
            return Link(rect, y, _thanksText, null, RegularFont, TextAnchor.UpperCenter,
                onClick: () => _thanksText += _thanksText == "you" ? " 🥰" : "🥰",
                spriteScale: 0.5f);
        }

        float NextSection(float y)
        {
            return y + HeadingGap;
        }

        float SectionHeading(Rect rect, float y, string text, int textSize = 0)
        {
            return Line(rect, y, (text ?? "").ToUpperInvariant(), GameFont.Medium,
                UiTheme.Dim,
                TextAnchor.UpperCenter,
                textSize > 0 ? textSize : SectionTextSize);
        }

        float CreditGrid(Rect rect, float y, Credit[] credits, int columns)
        {
            var detailStyle = SizedStyle(GameFont.Small, MetaTextSize, TextAnchor.MiddleRight, false);
            var nameStyle = SizedStyle(RegularFont, 0, TextAnchor.MiddleLeft, false);
            // Keep vertical glyph overflow; labels apply their own horizontal column clip.
            float line = Mathf.Max(UiFont.LineHeight(detailStyle), UiFont.LineHeight(nameStyle));
            float gap = ColumnGap;
            columns = Mathf.Clamp(Mathf.FloorToInt((rect.width + gap) / (240f + gap)), 1, columns);
            int rows = (credits.Length + columns - 1) / columns;
            var widths = new float[columns];
            float naturalWidth = gap * (columns - 1);
            for (int column = 0; column < columns; column++)
            {
                int start = column * rows;
                int end = Mathf.Min(credits.Length, start + rows);
                widths[column] = CreditColumnWidth(credits, start, end, detailStyle, nameStyle);
                naturalWidth += widths[column];
            }

            bool fits = naturalWidth <= rect.width;
            float colW = Mathf.Max(1f, (rect.width - gap * (columns - 1)) / columns);
            float x = fits ? rect.center.x - naturalWidth / 2f : rect.x;
            float maxHeight = 0f;
            for (int column = 0; column < columns; column++)
            {
                int start = column * rows;
                int end = Mathf.Min(credits.Length, start + rows);
                if (start >= end) continue;

                float width = fits ? widths[column] : colW;
                var columnRect = new Rect(x, y, width, 1f);
                maxHeight = Mathf.Max(maxHeight,
                    CreditColumn(columnRect, credits, start, end, detailStyle, nameStyle, line));
                x += width + gap;
            }

            return y + maxHeight;
        }

        float CreditColumnWidth(Credit[] credits, int start, int end,
            GUIStyle detailStyle, GUIStyle nameStyle)
        {
            float detailWidth = 0f;
            float nameWidth = 0f;
            for (int i = start; i < end; i++)
                detailWidth = Mathf.Max(detailWidth,
                    detailStyle.CalcSize(new GUIContent(credits[i].Detail)).x);

            for (int i = start; i < end; i++)
            {
                var credit = credits[i];
                if (credit.Links.Length == 0)
                {
                    nameWidth = Mathf.Max(nameWidth, nameStyle.CalcSize(new GUIContent(credit.Name)).x);
                    continue;
                }

                for (int link = 0; link < credit.Links.Length; link++)
                    nameWidth = Mathf.Max(nameWidth,
                        nameStyle.CalcSize(new GUIContent(credit.Links[link].Label)).x);
            }

            float sideWidth = Mathf.Max(detailWidth, nameWidth);
            return Mathf.Max(1f, sideWidth * 2f + RowGap * 2f);
        }

        float CreditColumn(Rect rect, Credit[] credits, int start, int end,
            GUIStyle detailStyle, GUIStyle nameStyle, float line)
        {
            float top = rect.y;
            float y = top;
            for (int i = start; i < end; i++)
                y = CreditRow(rect, y, credits[i], detailStyle, nameStyle, line);

            return y - top;
        }

        float CreditRow(Rect rect, float y, Credit credit, GUIStyle detailStyle, GUIStyle nameStyle,
            float line)
        {
            Text.Font = RegularFont;
            float nameHeight = line * Mathf.Max(1, credit.Links.Length);
            float middleGap = RowGap;
            float half = rect.width / 2f;
            var detail = new Rect(rect.x, y, Mathf.Max(1f, half - middleGap), line);
            var name = new Rect(rect.x + half + middleGap, y,
                Mathf.Max(1f, half - middleGap), line);

            // Both sides share a centered row and fractional scroll position. Native
            // RowLabel snaps to pixels, making only the names step during the credit roll.
            var wasColor = GUI.color;
            GUI.color = UiTheme.Dim;
            LabelWithHorizontalClip(detail, credit.Detail, detailStyle);
            GUI.color = wasColor;
            if (credit.Links.Length == 0)
            {
                GUI.color = UiTheme.Name;
                LabelWithHorizontalClip(name, credit.Name, nameStyle);
                GUI.color = wasColor;
            }
            else
                CreditNameWithLinks(name, y, credit, line, nameStyle);

            return y + nameHeight + RowGap;
        }

        void CreditNameWithLinks(Rect rect, float y, Credit credit, float line, GUIStyle nameStyle)
        {
            Text.Font = RegularFont;
            float x = rect.x;
            float remaining = rect.width;
            for (int i = 0; i < credit.Links.Length; i++)
            {
                var link = credit.Links[i];
                string label = link.Label ?? "";
                float width = Mathf.Min(nameStyle.CalcSize(new GUIContent(label)).x, remaining);
                if (width <= 0f) break;

                LinkAt(new Rect(x, y + i * line, width, line), label, link.Url,
                    RegularFont, nameStyle);
            }
        }

        float InlineLinkLine(Rect rect, float y, string before, string linked, string after,
            string url, GameFont font)
        {
            Text.Font = font;
            var style = SizedStyle(font, 0, TextAnchor.UpperLeft, false);
            float h = UiFont.LineHeight(style);
            float beforeWidth = style.CalcSize(new GUIContent(before)).x;
            float linkedWidth = style.CalcSize(new GUIContent(linked)).x;
            float afterWidth = style.CalcSize(new GUIContent(after)).x;
            float totalWidth = beforeWidth + linkedWidth + afterWidth;
            float x = rect.x + Mathf.Max(0f, (rect.width - totalWidth) / 2f);

            // Keep the built-with lines on the same fractional drawing path as credits.
            LabelAt(new Rect(x, y, beforeWidth, h), before, font, UiTheme.Name, style);
            LinkAt(new Rect(x + beforeWidth, y, linkedWidth, h), linked, url, font, style);
            LabelAt(new Rect(x + beforeWidth + linkedWidth, y, afterWidth, h), after, font,
                UiTheme.Name, style);
            return y + h;
        }

        void LabelAt(Rect rect, string text, GameFont font, Color color, GUIStyle style)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            LabelWithHorizontalClip(rect, text, style);
            GUI.color = wasColor;
            Text.Font = wasFont;
        }

        void LinkAt(Rect rect, string label, string url, GameFont font, GUIStyle style)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            bool over = Mouse.IsOver(rect);
            GUI.color = over ? UiTheme.Lead : UiTheme.Accent;
            LabelWithHorizontalClip(rect, label, style);
            if (UiButtons.RowButton(rect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Application.OpenURL(url);
            }

            GUI.color = wasColor;
            Text.Font = wasFont;
        }

        static void LabelWithHorizontalClip(Rect rect, string text, GUIStyle style)
        {
            // Unity's label clipping can cut dynamic-font descenders even in a measured row.
            // Clip the column separately, leaving vertical slack around the original label box.
            float slack = RowGap;
            GUI.BeginGroup(new Rect(rect.x, rect.y - slack, rect.width, rect.height + slack * 2f));
            try
            {
                GUI.Label(new Rect(0f, slack, rect.width, rect.height), text, style);
            }
            finally { GUI.EndGroup(); }
        }

        float Line(Rect r, float y, string text, GameFont font, Color color,
            TextAnchor anchor, int textSize = 0)
        {
            if (textSize <= 0)
                return NativeLine(r, y, text, font, color, anchor);

            var style = SizedStyle(font, textSize, anchor, false);
            float h = Mathf.Max(1f, style.CalcHeight(new GUIContent(text ?? ""), r.width));
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(r.x, y, r.width, h), text ?? "", style);
            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h;
        }

        float NativeLine(Rect r, float y, string text, GameFont font, Color color,
            TextAnchor anchor)
        {
            float h = UiTheme.LineHOf(font);
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            UiText.RowLabel(new Rect(r.x, y, r.width, h), text, anchor);
            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h;
        }

        float Paragraph(Rect r, float y, string text, GameFont font, Color color,
            TextAnchor anchor, int textSize = 0)
        {
            if (textSize > 0)
            {
                var style = SizedStyle(font, textSize, anchor, true);
                float largeH = Mathf.Max(1f,
                    style.CalcHeight(new GUIContent(text ?? ""), r.width)) +
                    RowGap;
                var largeColor = GUI.color;
                GUI.color = color;
                GUI.Label(new Rect(r.x, y, r.width, largeH), text ?? "", style);
                GUI.color = largeColor;
                return y + largeH;
            }

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            var wasWrap = Text.WordWrap;
            var wasAnchor = Text.Anchor;
            Text.Font = font;
            Text.WordWrap = true;
            Text.Anchor = anchor;
            float h = UiText.PlainStatusLabelHeight(text, r.width, font);
            UiText.PlainStatusLabel(new Rect(r.x, y, r.width, h), text, color, font, anchor);
            Text.Anchor = wasAnchor;
            Text.WordWrap = wasWrap;
            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h + RowGap;
        }

        float Link(Rect r, float y, string label, string url, GameFont font,
            TextAnchor anchor, int textSize = 0, Action onClick = null, float spriteScale = 1f)
        {
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            float h = UiTheme.LineHOf(font);
            GUIStyle style = textSize > 0 ? SizedStyle(font, textSize, TextAnchor.UpperLeft,
                false) : null;
            InlineTextLayout layout = null;
            if (spriteScale != 1f)
            {
                style = style ?? new GUIStyle(Text.CurFontStyle)
                {
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                };
                layout = InlineTextLayout.Proportional(label, TextSpriteCatalog.Shared,
                    h * spriteScale, plain => style.CalcSize(new GUIContent(plain)).x, r.width);
            }
            float width = layout != null ? layout.Width : textSize > 0
                ? Mathf.Min(style.CalcSize(new GUIContent(label ?? "")).x, r.width)
                : Mathf.Min(UiTheme.Wide(label), r.width);
            if (textSize > 0)
                h = Mathf.Max(1f, style.CalcHeight(new GUIContent(label ?? ""), width));
            float x = r.x;
            if (anchor == TextAnchor.UpperCenter) x += (r.width - width) / 2f;
            else if (anchor == TextAnchor.UpperRight) x += r.width - width;

            var hit = new Rect(x, y, width, h);
            bool over = Mouse.IsOver(hit);
            GUI.color = over ? UiTheme.Lead : UiTheme.Accent;
            if (layout != null) SharedTextRenderer.Draw(layout, hit, h, style);
            else if (textSize > 0) GUI.Label(hit, label ?? "", style);
            else UiText.RowLabel(hit, label, TextAnchor.UpperLeft);
            if (UiButtons.RowButton(hit))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                if (onClick != null) onClick();
                else Application.OpenURL(url);
            }

            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h + RowGap;
        }

        static GUIStyle SizedStyle(GameFont font, int textSize, TextAnchor anchor, bool wrap)
        {
            var wasFont = Text.Font;
            Text.Font = font;
            var style = new GUIStyle(Text.CurFontStyle)
            {
                alignment = anchor,
                clipping = TextClipping.Overflow,
                fontSize = textSize > 0 ? textSize : Text.CurFontStyle.fontSize,
                wordWrap = wrap,
            };
            Text.Font = wasFont;
            return style;
        }
    }
}
