using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace SlopWorld
{
    public sealed class AboutPage : IOptionPage
    {
        const GameFont RegularFont = GameFont.Medium;
        const float ContentPaddingX = 20f;
        const float ContentPaddingY = 16f;
        const float HeroMargin = UiWidgets.GapS;
        const float HeroIconSize = 88f;
        const float HeroIconGap = UiWidgets.GapXS;
        const float HeroTitleGap = UiWidgets.GapL;
        const float HeadingGap = UiWidgets.GapM;
        const float GroupGap = UiWidgets.GapS;
        const float RowGap = UiWidgets.GapXS;
        const float ColumnGap = UiWidgets.GapL;
        const float TailPadding = UiWidgets.GapL;
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

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _rimWorldScroll = new SmoothScroll();
        readonly Dialog_Options _rimWorldOptions = new Dialog_Options();
        float _contentHeight;
        float _rimWorldContentHeight;
        int _autoScrollFrame = -1;
        List<ListableOption> _links;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = rect.ContractedBy(UiWidgets.GapM, UiWidgets.GapM);

            float viewWidth = Mathf.Max(1f, inner.width - UiWidgets.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _contentHeight > 0f ? _contentHeight : FirstPassHeight);
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            using (_scroll.Scope(inner, view))
            {
                var content = new Rect(ContentPaddingX, ContentPaddingY,
                    Mathf.Max(1f, view.width - ContentPaddingX * 2f),
                    Mathf.Max(1f, view.height - ContentPaddingY * 2f));
                _contentHeight = DrawCredits(content) + ContentPaddingY;
            }

            AdvanceAutoScroll(inner, _contentHeight);
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

        public void DrawRimWorld(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = rect.ContractedBy(UiWidgets.GapM, UiWidgets.GapM);

            float viewWidth = Mathf.Max(1f, inner.width - UiWidgets.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _rimWorldContentHeight > 0f ? _rimWorldContentHeight : 1800f);
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            using (_rimWorldScroll.Scope(inner, view))
            {
                var content = new Rect(ContentPaddingX, ContentPaddingY,
                    Mathf.Max(1f, view.width - ContentPaddingX * 2f),
                    Mathf.Max(1f, view.height - ContentPaddingY * 2f));
                float y = DrawEulaDisclaimer(content, content.y);
                y = DrawRimWorldHeader(content, y);
                y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Graphics,
                    "DoVideoOptions");
                y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Interface,
                    "DoUIOptions");
                y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Controls,
                    "DoControlsOptions");
                _rimWorldContentHeight = y + ContentPaddingY;
            }
        }

        float DrawRimWorldHeader(Rect rect, float y)
        {
            float columnWidth = Mathf.Max(1f, (rect.width - ColumnGap) / 2f);
            var build = new Rect(rect.x, y, columnWidth, rect.height);
            var links = new Rect(rect.x + columnWidth + ColumnGap, y,
                columnWidth, rect.height);

            float buildBottom = DrawVersionInfo(build, build.y);
            float linksBottom = DrawWebLinks(links, links.y);
            return Mathf.Max(buildBottom, linksBottom) + HeadingGap;
        }

        float DrawRimWorldSection(Rect rect, float y, OptionCategoryDef category,
            string methodName)
        {
            if (category == null) return y;

            var method = typeof(Dialog_Options).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) return y;

            UiWidgets.SectionHeading(
                new Rect(rect.x, y, rect.width, UiWidgets.RowH), category.LabelCap);
            y += UiWidgets.RowH + UiWidgets.GapXS;

            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(new Rect(rect.x, y, rect.width, 10000f));
            listing.verticalSpacing = UiWidgets.GapXS;
            listing.Gap(UiWidgets.GapS + UiWidgets.GapXS);
            try
            {
                method.Invoke(_rimWorldOptions, new object[] { listing });
                float used = listing.CurHeight;
                listing.End();
                return y + used + HeadingGap;
            }
            catch
            {
                listing.End();
                throw;
            }
        }

        float DrawVersionInfo(Rect rect, float y)
        {
            float line = UiWidgets.LineH;
            float step = line + UiWidgets.GapXS;
            GUI.color = UiWidgets.Dim;
            UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, line), "RimWorld build");
            GUI.color = Color.white;
            y += step;

            UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                "VersionIndicator".Translate(
                    (NamedArgument)VersionControl.CurrentVersionString));
            y += step;
            UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                "CompiledOn".Translate(
                    (NamedArgument)VersionControl.CurrentBuildDate.ToString("MMM d yyyy")));
            y += step;

            if (SteamManager.Initialized)
            {
                y += UiWidgets.GapXS;
                UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                    "LoggedIntoSteamAs".Translate(
                        (NamedArgument)SteamUtility.SteamPersonaName));
                y += step;
            }

            y += UiWidgets.GapS;
            var lvg = Current.Root?.gameObject.GetComponent<LatestVersionGetter>();
            if (lvg != null)
            {
                lvg.DrawAt(new Rect(rect.x, y, rect.width, 50f));
                y += 50f + UiWidgets.GapXS;
            }

            return y + UiWidgets.GapS + UiWidgets.GapXS;
        }

        float DrawWebLinks(Rect rect, float y)
        {
            if (_links == null) _links = BuildLinks();

            return y + OptionListingUtility.DrawOptionListing(
                new Rect(rect.x, y, rect.width, 1000f), _links);
        }

        static List<ListableOption> BuildLinks()
        {
            return new List<ListableOption>
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
                        Find.WindowStack.Add(new UiMenu(opts));
                    },
                    TexButton.IconSoundtrack),
            };
        }

        float DrawCredits(Rect r)
        {
            float y = r.y;

            y = DrawEulaDisclaimer(r, y);
            y = DrawHero(r, y);
            y = DrawMusic(r, y);
            y = DrawBasedOn(r, y);
            y = DrawLibraries(r, y);
            y = DrawAssets(r, y);

            return y + TailPadding;
        }

        float DrawEulaDisclaimer(Rect r, float y)
        {
            y += HeroMargin;
            y = Paragraph(r, y, EulaDisclaimer, RegularFont, UiWidgets.Dim,
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
                y += HeroIconSize + HeroIconGap;
            }

            y = Line(r, y, "SlopWorld", GameFont.Medium, UiWidgets.Lead,
                TextAnchor.UpperCenter, TitleTextSize);
            y += HeroTitleGap;
            y = ByLine(r, y, "CREATED BY", "Lev Gorodetskii",
                "https://drsr.io/projects");
            return y + HeroMargin;
        }

        float ByLine(Rect r, float y, string role, string name, string url,
            int roleTextSize = 0)
        {
            y = Line(r, y, role, GameFont.Small, UiWidgets.Dim,
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
            y = InlineLinkLine(r, y, "", "Harmony by Andreas Pardeike", "",
                "https://github.com/pardeike/HarmonyRimWorld", RegularFont);
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
            y = SectionHeading(r, y, "Libraries", SubheadingTextSize) + HeadingGap;
            return CreditGrid(r, y, Libraries, 3);
        }

        float DrawAssets(Rect r, float y)
        {
            y = NextSection(y);
            y = SectionHeading(r, y, "Assets") + HeadingGap;
            return CreditGrid(r, y, Assets, 2);
        }

        float NextSection(float y)
        {
            return y + HeadingGap;
        }

        float SectionHeading(Rect rect, float y, string text, int textSize = 0)
        {
            return Line(rect, y, (text ?? "").ToUpperInvariant(), GameFont.Medium,
                UiWidgets.Dim,
                TextAnchor.UpperCenter,
                textSize > 0 ? textSize : SectionTextSize);
        }

        float CreditGrid(Rect rect, float y, Credit[] credits, int columns)
        {
            float gap = ColumnGap;
            columns = Mathf.Max(1, columns);
            int rows = (credits.Length + columns - 1) / columns;
            var widths = new float[columns];
            float naturalWidth = gap * (columns - 1);
            for (int column = 0; column < columns; column++)
            {
                int start = column * rows;
                int end = Mathf.Min(credits.Length, start + rows);
                widths[column] = CreditColumnWidth(credits, start, end);
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
                maxHeight = Mathf.Max(maxHeight, CreditColumn(columnRect, credits, start, end));
                x += width + gap;
            }

            return y + maxHeight;
        }

        float CreditColumnWidth(Credit[] credits, int start, int end)
        {
            float detailWidth = 0f;
            float nameWidth = 0f;
            var wasFont = Text.Font;

            Text.Font = GameFont.Small;
            for (int i = start; i < end; i++)
                detailWidth = Mathf.Max(detailWidth, UiWidgets.Wide(credits[i].Detail));

            Text.Font = RegularFont;
            for (int i = start; i < end; i++)
            {
                var credit = credits[i];
                if (credit.Links.Length == 0)
                {
                    nameWidth = Mathf.Max(nameWidth, UiWidgets.Wide(credit.Name));
                    continue;
                }

                for (int link = 0; link < credit.Links.Length; link++)
                    nameWidth = Mathf.Max(nameWidth,
                        UiWidgets.Wide(credit.Links[link].Label));
            }

            Text.Font = wasFont;
            float sideWidth = Mathf.Max(detailWidth, nameWidth);
            return Mathf.Max(1f, sideWidth * 2f + RowGap * 2f);
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
            float line = UiWidgets.LineHOf(RegularFont);
            float nameHeight = line * Mathf.Max(1, credit.Links.Length);
            float middleGap = RowGap;
            float half = rect.width / 2f;
            var detail = new Rect(rect.x, y, Mathf.Max(1f, half - middleGap), nameHeight);
            var name = new Rect(rect.x + half + middleGap, y,
                Mathf.Max(1f, half - middleGap), nameHeight);

            Line(detail, y, credit.Detail, GameFont.Small,
                UiWidgets.Dim, TextAnchor.UpperRight, MetaTextSize);
            if (credit.Links.Length == 0)
                Line(name, y, credit.Name, RegularFont, UiWidgets.Name,
                    TextAnchor.UpperLeft, BodyTextSize);
            else
                CreditNameWithLinks(name, y, credit);

            return y + nameHeight + RowGap;
        }

        void CreditNameWithLinks(Rect rect, float y, Credit credit)
        {
            Text.Font = RegularFont;
            float x = rect.x;
            float remaining = rect.width;
            float line = UiWidgets.LineHOf(RegularFont);
            for (int i = 0; i < credit.Links.Length; i++)
            {
                var link = credit.Links[i];
                string label = link.Label ?? "";
                float width = Mathf.Min(UiWidgets.Wide(label), remaining);
                if (width <= 0f) break;

                LinkAt(new Rect(x, y + i * line, width, line), label, link.Url,
                    RegularFont);
            }
        }

        float InlineLinkLine(Rect rect, float y, string before, string linked, string after,
            string url, GameFont font)
        {
            Text.Font = font;
            float h = UiWidgets.LineHOf(font);
            float beforeWidth = UiWidgets.Wide(before);
            float linkedWidth = UiWidgets.Wide(linked);
            float afterWidth = UiWidgets.Wide(after);
            float totalWidth = beforeWidth + linkedWidth + afterWidth;
            float x = rect.x + Mathf.Max(0f, (rect.width - totalWidth) / 2f);

            LabelAt(new Rect(x, y, beforeWidth, h), before, font, UiWidgets.Name);
            LinkAt(new Rect(x + beforeWidth, y, linkedWidth, h), linked, url, font);
            LabelAt(new Rect(x + beforeWidth + linkedWidth, y, afterWidth, h), after, font,
                UiWidgets.Name);
            return y + h;
        }

        void LabelAt(Rect rect, string text, GameFont font, Color color)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            UiWidgets.RowLabel(rect, text, TextAnchor.UpperLeft);
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
            GUI.color = over ? UiWidgets.Lead : UiWidgets.Accent;
            UiWidgets.RowLabel(rect, label, TextAnchor.UpperLeft);
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
            float h = UiWidgets.LineHOf(font);
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            UiWidgets.RowLabel(new Rect(r.x, y, r.width, h), text, anchor);
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
            GUI.color = color;
            float h = Mathf.Max(UiWidgets.LineHOf(font), Text.CalcHeight(text, r.width)) +
                RowGap;
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
            float h = UiWidgets.LineHOf(font);
            GUIStyle style = textSize > 0 ? SizedStyle(font, textSize, TextAnchor.UpperLeft,
                false) : null;
            float width = textSize > 0
                ? Mathf.Min(style.CalcSize(new GUIContent(label ?? "")).x, r.width)
                : Mathf.Min(UiWidgets.Wide(label), r.width);
            if (textSize > 0)
                h = Mathf.Max(1f, style.CalcHeight(new GUIContent(label ?? ""), width));
            float x = r.x;
            if (anchor == TextAnchor.UpperCenter) x += (r.width - width) / 2f;
            else if (anchor == TextAnchor.UpperRight) x += r.width - width;

            var hit = new Rect(x, y, width, h);
            bool over = Mouse.IsOver(hit);
            GUI.color = over ? UiWidgets.Lead : UiWidgets.Accent;
            if (textSize > 0) GUI.Label(hit, label ?? "", style);
            else UiWidgets.RowLabel(hit, label, TextAnchor.UpperLeft);
            if (Widgets.ButtonInvisible(hit))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Application.OpenURL(url);
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
                fontSize = Mathf.Max(1, textSize),
                wordWrap = wrap,
            };
            Text.Font = wasFont;
            return style;
        }
    }
}
