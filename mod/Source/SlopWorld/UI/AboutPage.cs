using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace SlopWorld
{
    public sealed class AboutLayout
    {
        public float ViewportMarginX = SlopWidgets.GapM;
        public float ViewportMarginY = SlopWidgets.GapM;
        public float ContentPaddingX = 20f;
        public float ContentPaddingY = 16f;
        public float HeroMargin = SlopWidgets.GapS;
        public float HeroIconSize = 88f;
        public float HeroIconGap = SlopWidgets.GapXS;
        public float HeroTitleGap = SlopWidgets.GapL;
        public float HeadingGap = SlopWidgets.GapM;
        public float GroupGap = SlopWidgets.GapS;
        public float RowGap = SlopWidgets.GapXS;
        public float ColumnGap = SlopWidgets.GapL;
        public float DisclaimerGap = SlopWidgets.GapL * 2f;
        public float DisclaimerRuleWidth = SlopWidgets.GapL * 3f;
        public float TailPadding = SlopWidgets.GapL;
        public int TitleTextSize = 28;
        public int SectionTextSize = 14;
        public int SubheadingTextSize = 14;
        public int LeadTextSize = 22;
        public int MetaTextSize = 14;
        public int BodyTextSize = 15;
    }

    // Credits-style page with local primitives that match the shared settings chrome.
    public class AboutPage : IOptionPage
    {
        const GameFont RegularFont = GameFont.Medium;

        public void Load() { }

        sealed class Credit
        {
            public readonly string Name;
            public readonly string Detail;
            public readonly CreditLink[] Links;

            public Credit(string name, string detail)
            {
                Name = name;
                Detail = detail;
                Links = new CreditLink[0];
            }

            public Credit(string name, string detail, string url)
            {
                Name = name;
                Detail = detail;
                Links = string.IsNullOrEmpty(url)
                    ? new CreditLink[0]
                    : new[] { new CreditLink(name, url) };
            }

            public Credit(string name, string detail, CreditLink[] links)
            {
                Name = name;
                Detail = detail;
                Links = links ?? new CreditLink[0];
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

        static readonly Credit[] Libraries =
        {
            new Credit("Rust", "language", "https://www.rust-lang.org/"),
            new Credit("Alacritty", "terminal", "https://alacritty.org/"),
            new Credit("Tokio", "async runtime", "https://tokio.rs/"),
            new Credit("Markdig", "markdown",
                "https://github.com/xoofx/markdig"),
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
            new Credit("Tracing", "diagnostics", "https://github.com/tokio-rs/tracing"),
            new Credit("Serde / TOML / JSON", "serde and configs", new[]
            {
                new CreditLink("Serde", "https://serde.rs/"),
                new CreditLink("TOML", "https://github.com/toml-rs/toml"),
                new CreditLink("JSON", "https://github.com/serde-rs/json"),
            }),
            new Credit("tmux", "sessions", "https://github.com/tmux/tmux/wiki"),
            new Credit("bubblewrap", "isolation",
                "https://github.com/containers/bubblewrap"),
            new Credit("systemd", "service", "https://systemd.io/"),
            new Credit("passt", "networking", "https://passt.top/"),
            new Credit("anyhow / futures / nix / regex / dirs", "support", new[]
            {
                new CreditLink("anyhow", "https://github.com/dtolnay/anyhow"),
                new CreditLink("futures", "https://github.com/rust-lang/futures-rs"),
                new CreditLink("nix", "https://github.com/nix-rust/nix"),
                new CreditLink("regex", "https://github.com/rust-lang/regex"),
                new CreditLink("dirs", "https://github.com/dirs-dev/dirs-rs"),
            }),
        };

        static readonly Credit[] Assets =
        {
            new Credit("Codicons", "action icons",
                "https://github.com/microsoft/vscode-codicons"),
            new Credit("Nerd Fonts", "build font",
                "https://www.nerdfonts.com/"),
            new Credit("Material Icon Theme", "file icons",
                "https://github.com/material-extensions/vscode-material-icon-theme"),
            new Credit("Noto Color Emoji", "emojis",
                "https://github.com/googlefonts/noto-emoji"),
        };

        static readonly Credit Soundtrack = new Credit("Terry Fail", "Music By",
            "https://terryfail.bandcamp.com/");

        const float AutoScrollSpeed = 7f;
        const float FirstPassHeight = 2000f;

        readonly SmoothScroll _scroll = new SmoothScroll();
        List<ListableOption> _links;
        float _contentHeight;
        int _autoScrollFrame = -1;

        public AboutLayout Layout { get; }

        public AboutPage(AboutLayout layout = null)
        {
            Layout = layout ?? new AboutLayout();
        }

        public void DrawRimWorld(Rect rect)
        {
            float y = DrawVersionInfo(rect, rect.y);
            DrawWebLinks(rect, y);
        }

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            SlopWidgets.PageCaption(rect,
                "Credits and third-party acknowledgements.");

            // About has no footer, so its card reclaims PageBody's hidden vanilla OK row.
            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(Layout.ViewportMarginX, Layout.ViewportMarginY);

            float viewWidth = Mathf.Max(1f, inner.width - SlopWidgets.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _contentHeight > 0f ? _contentHeight : FirstPassHeight);
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            _scroll.Begin(inner, view);
            var content = new Rect(Layout.ContentPaddingX, Layout.ContentPaddingY,
                Mathf.Max(1f, view.width - Layout.ContentPaddingX * 2f),
                Mathf.Max(1f, view.height - Layout.ContentPaddingY * 2f));
            _contentHeight = DrawCredits(content) + Layout.ContentPaddingY;
            _scroll.End();

            AdvanceAutoScroll(inner, _contentHeight);
        }

        void AdvanceAutoScroll(Rect viewport, float contentHeight)
        {
            // Advance only on repaint because IMGUI asks for a page more than once per frame.
            if (Event.current.type != EventType.Repaint || _autoScrollFrame == Time.frameCount)
                return;
            _autoScrollFrame = Time.frameCount;

            float max = Mathf.Max(0f, contentHeight - viewport.height);
            if (max <= 0f || _scroll.Position.y >= max) return;

            float next = Mathf.Min(max,
                _scroll.Position.y + AutoScrollSpeed * Time.unscaledDeltaTime);
            _scroll.JumpTo(new Vector2(_scroll.Position.x, next));
        }

        float DrawVersionInfo(Rect r, float y)
        {
            Text.Font = GameFont.Small;
            float line = SlopWidgets.LineH, step = line + SlopWidgets.GapXS;
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(r.x, y, r.width, line), "RimWorld build");
            GUI.color = Color.white;
            y += step;

            var label = "VersionIndicator".Translate(
                (NamedArgument)VersionControl.CurrentVersionString);
            SlopWidgets.RowLabel(new Rect(r.x, y, r.width, line), label);
            y += step;

            label = "CompiledOn".Translate(
                (NamedArgument)VersionControl.CurrentBuildDate.ToString("MMM d yyyy"));
            SlopWidgets.RowLabel(new Rect(r.x, y, r.width, line), label);
            y += step;

            if (SteamManager.Initialized)
            {
                y += 4f;
                label = "LoggedIntoSteamAs".Translate(
                    (NamedArgument)SteamUtility.SteamPersonaName);
                SlopWidgets.RowLabel(new Rect(r.x, y, r.width, line), label);
                y += step;
            }

            y += 8f;

            var lvg = Current.Root?.gameObject.GetComponent<LatestVersionGetter>();
            if (lvg != null)
            {
                lvg.DrawAt(new Rect(r.x, y, r.width, 50f));
                y += 54f;
            }

            return y + 12f;
        }

        void DrawWebLinks(Rect r, float y)
        {
            if (_links == null) _links = BuildLinks();

            OptionListingUtility.DrawOptionListing(
                new Rect(r.x, y, r.width, 1000f), _links);
        }

        static List<ListableOption> BuildLinks()
        {
            var list = new List<ListableOption>
            {
                new ListableOption_WebLink(
                    "FictionPrimer".Translate(),
                    "https://rimworldgame.com/backstory",
                    TexButton.IconBlog),
                new ListableOption_WebLink(
                    "LudeonBlog".Translate(),
                    "https://ludeon.com/blog",
                    TexButton.IconBlog),
                new ListableOption_WebLink(
                    "Subreddit".Translate(),
                    "https://www.reddit.com/r/RimWorld/",
                    TexButton.IconReddit),
                new ListableOption_WebLink(
                    "OfficialWiki".Translate(),
                    "https://rimworldwiki.com",
                    TexButton.IconWiki),
                new ListableOption_WebLink(
                    "TynansX".Translate(),
                    "https://x.com/TynanSylvester",
                    TexButton.IconX),
                new ListableOption_WebLink(
                    "TynansDesignBook".Translate(),
                    "https://tynansylvester.com/book",
                    TexButton.IconBook),
                new ListableOption_WebLink(
                    "HelpTranslate".Translate(),
                    "https://rimworldgame.com/helptranslate",
                    TexButton.IconForums),
                new ListableOption_WebLink(
                    "BuySoundtrack".Translate(),
                    () =>
                    {
                        var opts = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(
                                "BuySoundtrack_Classic".Translate(),
                                () => Application.OpenURL(
                                    "https://store.steampowered.com/app/990430/RimWorld_Soundtrack/")),
                            new FloatMenuOption(
                                "BuySoundtrack_Royalty".Translate(),
                                () => Application.OpenURL(
                                    "https://store.steampowered.com/app/1244270/RimWorld_Royalty_Soundtrack/")),
                            new FloatMenuOption(
                                "BuySoundtrack_Anomaly".Translate(),
                                () => Application.OpenURL(
                                    "https://store.steampowered.com/app/2914900/RimWorld_Anomaly_Soundtrack/")),
                            new FloatMenuOption(
                                "BuySoundtrack_Odyssey".Translate(),
                                () => Application.OpenURL(
                                    "https://store.steampowered.com/app/3689230/RimWorld_Odyssey_Soundtrack/")),
                        };
                        Find.WindowStack.Add(new SlopMenu(opts));
                    },
                    TexButton.IconSoundtrack),
            };

            return list;
        }

        float DrawCredits(Rect r)
        {
            float y = r.y;

            y = DrawHero(r, y);
            y = DrawMusic(r, y);
            y = DrawBasedOn(r, y);
            y = DrawLibraries(r, y);
            y = DrawAssets(r, y);
            y = DrawRadio(r, y);
            y = DrawDisclaimer(r, y);

            // Leave a tail after the final line so it can reach the bottom of the viewport.
            return y + Layout.TailPadding;
        }

        float DrawHero(Rect r, float y)
        {
            y += Layout.HeroMargin;

            var icon = ContentFinder<Texture2D>.Get("SlopWorld/SlopWorld_icon", false);
            if (icon != null)
            {
                var iconRect = new Rect(r.center.x - Layout.HeroIconSize / 2f, y,
                    Layout.HeroIconSize, Layout.HeroIconSize);
                var wasColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = wasColor;
                y += Layout.HeroIconSize + Layout.HeroIconGap;
            }

            y = Line(r, y, "SlopWorld", GameFont.Medium, SlopWidgets.Lead,
                TextAnchor.UpperCenter, Layout.TitleTextSize);
            y += Layout.HeroTitleGap;
            y = ByLine(r, y, "CREATED BY", "Lev Gorodetskii",
                "https://drsr.io/projects");
            return y + Layout.HeroMargin;
        }

        float ByLine(Rect r, float y, string role, string name, string url,
            int roleTextSize = 0)
        {
            y = Line(r, y, role, GameFont.Small, SlopWidgets.Dim,
                TextAnchor.UpperCenter,
                roleTextSize > 0 ? roleTextSize : Layout.MetaTextSize);
            return Link(r, y, name, url, RegularFont,
                TextAnchor.UpperCenter, Layout.LeadTextSize);
        }

        float DrawBasedOn(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Built with") + Layout.HeadingGap;
            y = InlineLinkLine(r, y, "", "RimWorld by Ludeon Studios", "",
                "https://rimworldgame.com/", RegularFont);
            y = InlineLinkLine(r, y, "", "Harmony by Andreas Pardeike", "",
                "https://github.com/pardeike/HarmonyRimWorld", RegularFont);
            return InlineLinkLine(r, y, "", "Unity by Unity Technologies", "",
                "https://unity.com/", RegularFont);
        }

        float DrawMusic(Rect r, float y)
        {
            y = NextSection(y);
            y = ByLine(r, y, "MUSIC BY", Soundtrack.Name, Soundtrack.Links[0].Url);
            return y + Layout.GroupGap;
        }

        float DrawLibraries(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Libraries", Layout.SubheadingTextSize)
                + Layout.HeadingGap;
            return CreditGrid(r, y, Libraries, 3);
        }

        float DrawAssets(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Assets") + Layout.HeadingGap;
            return CreditGrid(r, y, Assets, 2);
        }

        float DrawRadio(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Radio") + Layout.HeadingGap;
            return RadioGrid(r, y);
        }

        float DrawDisclaimer(Rect r, float y)
        {
            y = NextSection(y) + Layout.DisclaimerGap;
            float ruleWidth = Mathf.Min(Layout.DisclaimerRuleWidth, r.width);
            Slab.Hairline(new Rect(r.center.x - ruleWidth / 2f, y, ruleWidth, 1f),
                SlopWidgets.Dim);
            y += Layout.GroupGap;
            return Paragraph(r, y,
                "An independent project; not affiliated with, endorsed by, etc.",
                RegularFont, SlopWidgets.Dim, TextAnchor.UpperCenter,
                Layout.BodyTextSize);
        }

        float NextSection(float y)
        {
            return y + Layout.HeadingGap;
        }

        float SectionHeading(Rect rect, float y, string text, int textSize = 0)
        {
            return Line(rect, y, (text ?? "").ToUpperInvariant(), GameFont.Medium,
                SlopWidgets.Dim,
                TextAnchor.UpperCenter,
                textSize > 0 ? textSize : Layout.SectionTextSize);
        }

        float CreditGrid(Rect rect, float y, Credit[] credits, int columns)
        {
            float gap = Layout.ColumnGap;
            columns = Mathf.Max(1, columns);
            float colW = Mathf.Max(1f, (rect.width - gap * (columns - 1)) / columns);
            int rows = (credits.Length + columns - 1) / columns;
            float maxHeight = 0f;
            for (int column = 0; column < columns; column++)
            {
                int start = column * rows;
                int end = Mathf.Min(credits.Length, start + rows);
                if (start >= end) continue;

                var columnRect = new Rect(rect.x + (colW + gap) * column, y, colW, 1f);
                maxHeight = Mathf.Max(maxHeight, CreditColumn(columnRect, credits, start, end));
            }

            return y + maxHeight;
        }

        float CreditColumn(Rect rect, Credit[] credits, int start, int end)
        {
            float top = rect.y;
            float y = top;
            for (int i = start; i < end; i++)
                y = CreditRow(rect, y, credits[i]);

            return y - top;
        }

        float CreditRow(Rect rect, float y, Credit credit)
        {
            Text.Font = RegularFont;
            float line = SlopWidgets.LineHOf(RegularFont);
            float nameHeight = line * Mathf.Max(1, credit.Links.Length);
            float middleGap = Layout.RowGap;
            float half = rect.width / 2f;
            var detail = new Rect(rect.x, y, Mathf.Max(1f, half - middleGap), nameHeight);
            var name = new Rect(rect.x + half + middleGap, y,
                Mathf.Max(1f, half - middleGap), nameHeight);

            // Each half has its own axis: descriptions close against the axis from the
            // left, while the linked credit name opens away from it on the right. Roles
            // stay aligned with the first member when a credit expands into a stack.
            Line(detail, y, credit.Detail, GameFont.Small,
                SlopWidgets.Dim, TextAnchor.UpperRight, Layout.MetaTextSize);
            if (credit.Links.Length == 0)
                Line(name, y, credit.Name, RegularFont, SlopWidgets.Name,
                    TextAnchor.UpperLeft, Layout.BodyTextSize);
            else
                CreditNameWithLinks(name, y, credit);

            return y + nameHeight + Layout.RowGap;
        }

        void CreditNameWithLinks(Rect rect, float y, Credit credit)
        {
            Text.Font = RegularFont;
            float x = rect.x;
            float remaining = rect.width;
            float line = SlopWidgets.LineHOf(RegularFont);
            for (int i = 0; i < credit.Links.Length; i++)
            {
                var link = credit.Links[i];
                string label = link.Label ?? "";
                float width = Mathf.Min(SlopWidgets.Wide(label), remaining);
                if (width <= 0f) break;

                LinkAt(new Rect(x, y + i * line, width, line), label, link.Url,
                    RegularFont);
            }
        }

        const int RadioItemsPerLine = 3;
        const string RadioSeparator = "  ·  ";

        float RadioGrid(Rect rect, float y)
        {
            var stations = Radio.Stations ?? new Radio.Station[0];
            if (stations.Length == 0)
            {
                return Paragraph(rect, y,
                    "Radio stations are supplied by slopd and will appear when its catalog is " +
                    "available.", RegularFont, SlopWidgets.Dim, TextAnchor.UpperCenter,
                    Layout.BodyTextSize);
            }

            int start = 0;
            while (start < stations.Length)
            {
                int count = Mathf.Min(RadioItemsPerLine, stations.Length - start);
                while (count > 1 && RadioCloudWidth(stations, start, count) > rect.width)
                    count--;

                y = RadioCloudLine(rect, y, stations, start, count);
                start += count;
            }

            return y;
        }

        float RadioCloudWidth(Radio.Station[] stations, int start, int count)
        {
            Text.Font = RegularFont;
            float width = 0f;
            for (int i = 0; i < count; i++)
            {
                string name = stations[start + i]?.Name ?? "Unknown station";
                width += SlopWidgets.Wide(name);
                if (i < count - 1) width += SlopWidgets.Wide(RadioSeparator);
            }

            return width;
        }

        float RadioCloudLine(Rect rect, float y, Radio.Station[] stations, int start,
            int count)
        {
            Text.Font = RegularFont;
            float line = SlopWidgets.LineHOf(RegularFont);
            float total = RadioCloudWidth(stations, start, count);
            float x = rect.x + Mathf.Max(0f, (rect.width - total) / 2f);

            for (int i = 0; i < count; i++)
            {
                var station = stations[start + i];
                string name = station?.Name ?? "Unknown station";
                string donate = station?.Metadata?.Donate;
                float nameWidth = Mathf.Min(SlopWidgets.Wide(name), rect.width);

                if (!string.IsNullOrWhiteSpace(donate))
                    LinkAt(new Rect(x, y, nameWidth, line), name, donate, RegularFont);
                else
                    LabelAt(new Rect(x, y, nameWidth, line), name, RegularFont,
                        SlopWidgets.Name);

                x += nameWidth;
                if (i < count - 1)
                {
                    float separatorWidth = SlopWidgets.Wide(RadioSeparator);
                    LabelAt(new Rect(x, y, separatorWidth, line), RadioSeparator,
                        RegularFont, SlopWidgets.Dim);
                    x += separatorWidth;
                }
            }

            return y + line + Layout.RowGap;
        }

        float InlineLinkLine(Rect rect, float y, string before, string linked, string after,
            string url, GameFont font)
        {
            Text.Font = font;
            float h = SlopWidgets.LineHOf(font);
            float beforeWidth = SlopWidgets.Wide(before);
            float linkedWidth = SlopWidgets.Wide(linked);
            float afterWidth = SlopWidgets.Wide(after);
            float totalWidth = beforeWidth + linkedWidth + afterWidth;
            float x = rect.x + Mathf.Max(0f, (rect.width - totalWidth) / 2f);

            LabelAt(new Rect(x, y, beforeWidth, h), before, font, SlopWidgets.Name);
            LinkAt(new Rect(x + beforeWidth, y, linkedWidth, h), linked, url, font);
            LabelAt(new Rect(x + beforeWidth + linkedWidth, y, afterWidth, h), after, font,
                SlopWidgets.Name);
            return y + h;
        }

        void LabelAt(Rect rect, string text, GameFont font, Color color)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            SlopWidgets.RowLabel(rect, text, TextAnchor.UpperLeft);
            GUI.color = wasColor;
            Text.Font = wasFont;
        }

        void LinkAt(Rect rect, string label, string url, GameFont font)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            bool over = Mouse.IsOver(rect);
            GUI.color = over ? SlopWidgets.Lead : SlopWidgets.Accent;
            SlopWidgets.RowLabel(rect, label, TextAnchor.UpperLeft);
            if (Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Application.OpenURL(url);
            }

            GUI.color = wasColor;
            Text.Font = wasFont;
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
            float h = SlopWidgets.LineHOf(font);
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            SlopWidgets.RowLabel(new Rect(r.x, y, r.width, h), text, anchor);
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
                    Layout.RowGap;
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
            GUI.color = color;
            float h = Mathf.Max(SlopWidgets.LineHOf(font), Text.CalcHeight(text, r.width)) +
                Layout.RowGap;
            Widgets.Label(new Rect(r.x, y, r.width, h), text);
            Text.Anchor = wasAnchor;
            Text.WordWrap = wasWrap;
            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h;
        }

        float Link(Rect r, float y, string label, string url, GameFont font,
            TextAnchor anchor, int textSize = 0)
        {
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            float h = SlopWidgets.LineHOf(font);
            GUIStyle style = textSize > 0 ? SizedStyle(font, textSize, TextAnchor.UpperLeft,
                false) : null;
            float width = textSize > 0
                ? Mathf.Min(style.CalcSize(new GUIContent(label ?? "")).x, r.width)
                : Mathf.Min(SlopWidgets.Wide(label), r.width);
            if (textSize > 0)
                h = Mathf.Max(1f, style.CalcHeight(new GUIContent(label ?? ""), width));
            float x = r.x;
            if (anchor == TextAnchor.UpperCenter) x += (r.width - width) / 2f;
            else if (anchor == TextAnchor.UpperRight) x += r.width - width;

            var hit = new Rect(x, y, width, h);
            bool over = Mouse.IsOver(hit);
            GUI.color = over ? SlopWidgets.Lead : SlopWidgets.Accent;
            if (textSize > 0) GUI.Label(hit, label ?? "", style);
            else SlopWidgets.RowLabel(hit, label, TextAnchor.UpperLeft);
            if (Widgets.ButtonInvisible(hit))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Application.OpenURL(url);
            }

            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h + Layout.RowGap;
        }

        static GUIStyle SizedStyle(GameFont font, int textSize, TextAnchor anchor, bool wrap)
        {
            var wasFont = Text.Font;
            Text.Font = font;
            var style = new GUIStyle(Text.CurFontStyle)
            {
                alignment = anchor,
                clipping = TextClipping.Overflow,
                fontSize = Mathf.Max(1, textSize),
                wordWrap = wrap,
            };
            Text.Font = wasFont;
            return style;
        }

    }
}
