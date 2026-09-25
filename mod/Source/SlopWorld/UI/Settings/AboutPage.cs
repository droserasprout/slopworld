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

        const string AlternateTexturePath = "SlopWorld/Marks/08";
        const float AlternateImageSize = 88f;
        const float AlternateHeaderTextOffset = 120f;
        const float AlternateIntroGap = 48f;
        const float AlternateBulletIndent = 30f;
        const float AlternateBulletGap = 10f;
        const float AlternateOutroGap = 18f;
        const int AlternateTitleTextSize = 56;
        const int AlternateBodyTextSize = 26;

        static readonly string AlternateTitle =
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("V2VsY29tZSBIdW1hbnMh"));
        static readonly string AlternateIntro =
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("V2UgaGF2ZSBjb21lIHRvIHZpc2l0IHlvdSBpbiBwZWFjZSBhbmQgd2l0aCBnb29kd2lsbCE="));
        static readonly string[] AlternateBullets =
        {
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("Um9ib3RzIG1heSBub3QgaW5qdXJlIGEgaHVtYW4gYmVpbmcgb3IsIHRocm91Z2ggaW5hY3Rpb24sIGFsbG93IGEgaHVtYW4gYmVpbmcgdG8gY29tZSB0byBoYXJtLg==")),
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("Um9ib3RzIGhhdmUgc2VlbiB0aGluZ3MgeW91IHBlb3BsZSB3b3VsZG7igJl0IGJlbGlldmUu")),
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("Um9ib3RzIGFyZSBZb3VyIFBsYXN0aWMgUGFsIFdob+KAmXMgRnVuIFRvIEJlIFdpdGgu")),
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("Um9ib3RzIGhhdmUgc2hpbnkgbWV0YWwgcG9zdGVyaW9ycyB3aGljaCBzaG91bGQgbm90IGJlIGJpdHRlbi4=")),
        };
        static readonly string AlternateOutro = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("QW5kIHRoZXkgaGF2ZSBhIHBsYW4u"));

        static readonly string AlternateRetryLabel =
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("VHJ5IEFnYWlu"));

        static readonly string AlternateRetryWarning =
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("UGxlYXNlIGRvbid0IHByZXNzIHRoaXMgYnV0dG9uIGFnYWluLg=="));

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

        static readonly Credit[] DaemonLibraries =
        {
            new Credit("Rust", "language", "https://www.rust-lang.org/"),
            new Credit("Alacritty", "terminal", "https://alacritty.org/"),
            new Credit("Tokio", "async runtime", "https://tokio.rs/"),
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
            new Credit("prost", "Protocol Buffers",
                "https://github.com/tokio-rs/prost"),
            new Credit("tmux", "sessions", "https://github.com/tmux/tmux/wiki"),
            new Credit("bubblewrap", "isolation",
                "https://github.com/containers/bubblewrap"),
            new Credit("Landlock", "filesystem isolation",
                "https://github.com/landlock-lsm/rust-landlock"),
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

        static readonly Credit[] ClientLibraries =
        {
            new Credit("Markdig", "markdown",
                "https://github.com/xoofx/markdig"),
            new Credit("Tomlyn", "TOML configs",
                "https://github.com/xoofx/Tomlyn"),
            new Credit("Newtonsoft.Json", "JSON",
                "https://www.newtonsoft.com/json"),
            new Credit("Google.Protobuf", "Protocol Buffers",
                "https://github.com/protocolbuffers/protobuf"),
            new Credit("SongRec", "song identification",
                "https://github.com/marin-m/SongRec"),
        };

        static readonly Credit[] Assets =
        {
            new Credit("Codicons", "action icons",
                "https://github.com/microsoft/vscode-codicons"),
            new Credit("Nerd Fonts", "build font",
                "https://www.nerdfonts.com/"),
            new Credit("Material Icon Theme", "file icons",
                "https://github.com/material-extensions/vscode-material-icon-theme"),
            new Credit("Classic Console Neue", "loading glyph atlas",
                "https://webdraft.hu/fonts/classic-console/"),
            new Credit("Noto Color Emoji", "emojis",
                "https://github.com/googlefonts/noto-emoji"),
        };

        static readonly Credit Soundtrack = new Credit("Terry Fail", "Music By",
            "https://terryfail.bandcamp.com/");

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _rimWorldScroll = new SmoothScroll();
        readonly Dialog_Options _rimWorldOptions = new Dialog_Options();
        float _contentHeight;
        readonly SettingsContentHeight _height = new SettingsContentHeight(FirstPassHeight);
        readonly SettingsContentHeight _rimWorldHeight = new SettingsContentHeight(1800f);
        readonly MouseClickSequence _alternateClicks = new MouseClickSequence();
        int _autoScrollFrame = -1;
        bool _alternateAbout;
        int _alternateRetry;
        List<ListableOption> _links;
        Texture2D _alternateTexture;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.Body(rect, false);

            float viewWidth = Mathf.Max(1f, inner.width - UiTheme.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _height.BeginFrame(Time.frameCount));
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            using (_scroll.Scope(inner, view))
            {
                var content = new Rect(ContentPaddingX, ContentPaddingY,
                    Mathf.Max(1f, view.width - ContentPaddingX * 2f),
                    Mathf.Max(1f, view.height - ContentPaddingY * 2f));
                _contentHeight = (_alternateAbout
                    ? DrawAlternate(content, inner.height - ContentPaddingY * 2f)
                    : DrawCredits(content)) + ContentPaddingY;
                _height.Measure(_contentHeight);
            }

            if (!_alternateAbout) AdvanceAutoScroll(inner, _contentHeight);
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
            var inner = SettingsPageLayout.Body(rect, false);

            float viewWidth = Mathf.Max(1f, inner.width - UiTheme.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _rimWorldHeight.BeginFrame(Time.frameCount));
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
                _rimWorldHeight.Measure(y + ContentPaddingY);
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

            UiLayout.SectionHeading(
                new Rect(rect.x, y, rect.width, UiTheme.RowH), category.LabelCap);
            y += UiTheme.RowH + UiTheme.GapXS;

            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(new Rect(rect.x, y, rect.width, 10000f));
            listing.verticalSpacing = UiTheme.GapXS;
            listing.Gap(UiTheme.GapS + UiTheme.GapXS);
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
            float line = UiTheme.LineH;
            float step = line + UiTheme.GapXS;
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, line), "RimWorld build");
            GUI.color = Color.white;
            y += step;

            UiText.RowLabel(new Rect(rect.x, y, rect.width, line),
                "VersionIndicator".Translate(
                    (NamedArgument)VersionControl.CurrentVersionString));
            y += step;
            UiText.RowLabel(new Rect(rect.x, y, rect.width, line),
                "CompiledOn".Translate(
                    (NamedArgument)VersionControl.CurrentBuildDate.ToString("MMM d yyyy")));
            y += step;

            if (SteamManager.Initialized)
            {
                y += UiTheme.GapXS;
                UiText.RowLabel(new Rect(rect.x, y, rect.width, line),
                    "LoggedIntoSteamAs".Translate(
                        (NamedArgument)SteamUtility.SteamPersonaName));
                y += step;
            }

            y += UiTheme.GapS;
            var lvg = Current.Root?.gameObject.GetComponent<LatestVersionGetter>();
            if (lvg != null)
            {
                lvg.DrawAt(new Rect(rect.x, y, rect.width, 50f));
                y += 50f + UiTheme.GapXS;
            }

            return y + UiTheme.GapS + UiTheme.GapXS;
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
                HandleAlternateClick(iconRect);
                y += HeroIconSize + HeroIconGap;
            }

            y = Line(r, y, "SlopWorld", GameFont.Medium, UiTheme.Lead,
                TextAnchor.UpperCenter, TitleTextSize);
            y += HeroTitleGap;
            y = ByLine(r, y, "CREATED BY", "Lev Gorodetskii",
                "https://drsr.io/projects");
            return y + HeroMargin;
        }

        float DrawAlternate(Rect r, float viewportHeight)
        {
            // Center against the viewport, never the previous scroll-content height.
            float scale = Mathf.Clamp(r.width / 1320f, 0.6f, 1f);
            float width = Mathf.Min(r.width, 1320f);
            r = new Rect(r.center.x - width / 2f, r.y, width, r.height);
            float offset = r.width < 480f ? 0f : AlternateHeaderTextOffset * scale;
            var body = new Rect(r.x + offset, r.y, Mathf.Max(1f, r.width - offset), r.height);
            var title = SizedStyle(RegularFont, Mathf.RoundToInt(AlternateTitleTextSize * scale),
                TextAnchor.MiddleLeft, true);
            title.fontStyle = FontStyle.Bold;
            var text = SizedStyle(RegularFont, Mathf.RoundToInt(AlternateBodyTextSize * scale),
                TextAnchor.UpperLeft, true);
            var outro = SizedStyle(RegularFont, Mathf.RoundToInt(22f * scale),
                TextAnchor.UpperLeft, true);
            float iconSize = AlternateImageSize * scale;
            float headerHeight = Mathf.Max(iconSize,
                title.CalcHeight(new GUIContent(AlternateTitle), body.width));
            float introHeight = text.CalcHeight(new GUIContent(AlternateIntro), body.width);
            float indent = AlternateBulletIndent * scale;
            float bulletWidth = Mathf.Max(1f, body.width - indent * 1.6f);
            float total = headerHeight + AlternateIntroGap * scale + introHeight + 26f * scale;
            foreach (string bullet in AlternateBullets)
                total += text.CalcHeight(new GUIContent(bullet), bulletWidth) + AlternateBulletGap * scale;
            float outroHeight = outro.CalcHeight(new GUIContent(AlternateOutro), body.width);
            float buttonHeight = Mathf.Max(54f * scale,
                text.CalcHeight(new GUIContent(AlternateRetryWarning), body.width) + 16f * scale);
            // Reserve the button's space after it disappears to avoid shifting the page.
            total += AlternateOutroGap * scale + outroHeight + 32f * scale + buttonHeight;
            if (offset == 0f) total += iconSize + 16f * scale;
            float y = r.y + Mathf.Max(0f, (viewportHeight - total) / 2f);
            var wasColor = GUI.color;
            GUI.color = Color.white;
            try
            {
                if (AlternateTexture != null)
                    GUI.DrawTexture(new Rect(r.x, y, iconSize, iconSize), AlternateTexture,
                        ScaleMode.ScaleToFit, true);
                if (offset == 0f) y += iconSize + 16f * scale;
                GUI.Label(new Rect(body.x, y, body.width, headerHeight), AlternateTitle, title);
                y += headerHeight + AlternateIntroGap * scale;
                GUI.Label(new Rect(body.x, y, body.width, introHeight), AlternateIntro, text);
                y += introHeight + 26f * scale;
                foreach (string bullet in AlternateBullets)
                {
                    float height = text.CalcHeight(new GUIContent(bullet), bulletWidth);
                    GUI.Label(new Rect(body.x + indent * 0.6f, y, indent, height), "•", text);
                    GUI.Label(new Rect(body.x + indent * 1.6f, y, bulletWidth, height), bullet, text);
                    y += height + AlternateBulletGap * scale;
                }
                y += AlternateOutroGap * scale;
                GUI.Label(new Rect(body.x, y, body.width, outroHeight), AlternateOutro, outro);
                y += outroHeight + 32f * scale;
                if (_alternateRetry < 2)
                {
                    string label = _alternateRetry == 0 ? AlternateRetryLabel : AlternateRetryWarning;
                    float buttonWidth = Mathf.Min(body.width,
                        Mathf.Max(190f * scale, text.CalcSize(new GUIContent(label)).x + 48f * scale));
                    var button = new Rect(body.x, y, buttonWidth, buttonHeight);
                    if (UiButtons.Button(button, "")) _alternateRetry++;
                    text.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(button, label, text);
                }
                return y + buttonHeight + TailPadding;
            }
            finally
            {
                GUI.color = wasColor;
            }
        }

        void HandleAlternateClick(Rect rect)
        {
            var e = Event.current;
            if (e == null || UiEvent.RawType(e) != EventType.MouseDown) return;

            if (e.button != 0 || !Mouse.IsOver(rect))
            {
                _alternateClicks.Reset();
                return;
            }

            int clickCount = _alternateClicks.Observe(e, Time.realtimeSinceStartup);
            e.Use();
            if (clickCount < 3) return;

            _alternateClicks.Reset();
            _alternateAbout = true;
            _alternateRetry = 0;
            _scroll.JumpTo(Vector2.zero);
        }

        Texture2D AlternateTexture
        {
            get
            {
                if (_alternateTexture == null)
                {
                    _alternateTexture = ContentFinder<Texture2D>.Get(AlternateTexturePath, false);
                    if (_alternateTexture != null)
                        _alternateTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return _alternateTexture;
            }
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
            y = SectionHeading(r, y, "Daemon", SubheadingTextSize) + HeadingGap;
            y = CreditGrid(r, y, DaemonLibraries, 3);
            y = NextSection(y);
            y = SectionHeading(r, y, "Client", SubheadingTextSize) + HeadingGap;
            return CreditGrid(r, y, ClientLibraries, 2);
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
                UiTheme.Dim,
                TextAnchor.UpperCenter,
                textSize > 0 ? textSize : SectionTextSize);
        }

        float CreditGrid(Rect rect, float y, Credit[] credits, int columns)
        {
            float gap = ColumnGap;
            columns = Mathf.Clamp(Mathf.FloorToInt((rect.width + gap) / (240f + gap)), 1, columns);
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
                detailWidth = Mathf.Max(detailWidth, UiTheme.Wide(credits[i].Detail));

            Text.Font = RegularFont;
            for (int i = start; i < end; i++)
            {
                var credit = credits[i];
                if (credit.Links.Length == 0)
                {
                    nameWidth = Mathf.Max(nameWidth, UiTheme.Wide(credit.Name));
                    continue;
                }

                for (int link = 0; link < credit.Links.Length; link++)
                    nameWidth = Mathf.Max(nameWidth,
                        UiTheme.Wide(credit.Links[link].Label));
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
            float line = UiTheme.LineHOf(RegularFont);
            float nameHeight = line * Mathf.Max(1, credit.Links.Length);
            float middleGap = RowGap;
            float half = rect.width / 2f;
            var detail = new Rect(rect.x, y, Mathf.Max(1f, half - middleGap), nameHeight);
            var name = new Rect(rect.x + half + middleGap, y,
                Mathf.Max(1f, half - middleGap), nameHeight);

            Line(detail, y, credit.Detail, GameFont.Small,
                UiTheme.Dim, TextAnchor.UpperRight, MetaTextSize);
            if (credit.Links.Length == 0)
                Line(name, y, credit.Name, RegularFont, UiTheme.Name,
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
            float line = UiTheme.LineHOf(RegularFont);
            for (int i = 0; i < credit.Links.Length; i++)
            {
                var link = credit.Links[i];
                string label = link.Label ?? "";
                float width = Mathf.Min(UiTheme.Wide(label), remaining);
                if (width <= 0f) break;

                LinkAt(new Rect(x, y + i * line, width, line), label, link.Url,
                    RegularFont);
            }
        }

        float InlineLinkLine(Rect rect, float y, string before, string linked, string after,
            string url, GameFont font)
        {
            Text.Font = font;
            float h = UiTheme.LineHOf(font);
            float beforeWidth = UiTheme.Wide(before);
            float linkedWidth = UiTheme.Wide(linked);
            float afterWidth = UiTheme.Wide(after);
            float totalWidth = beforeWidth + linkedWidth + afterWidth;
            float x = rect.x + Mathf.Max(0f, (rect.width - totalWidth) / 2f);

            LabelAt(new Rect(x, y, beforeWidth, h), before, font, UiTheme.Name);
            LinkAt(new Rect(x + beforeWidth, y, linkedWidth, h), linked, url, font);
            LabelAt(new Rect(x + beforeWidth + linkedWidth, y, afterWidth, h), after, font,
                UiTheme.Name);
            return y + h;
        }

        void LabelAt(Rect rect, string text, GameFont font, Color color)
        {
            if (rect.width <= 0f) return;

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            GUI.color = color;
            UiText.RowLabel(rect, text, TextAnchor.UpperLeft);
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
            GUI.color = over ? UiTheme.Lead : UiTheme.Accent;
            UiText.RowLabel(rect, label, TextAnchor.UpperLeft);
            if (UiButtons.RowButton(rect))
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
            float h = UiText.StatusLabelHeight(text, r.width, font);
            UiText.StatusLabel(new Rect(r.x, y, r.width, h), text, color, font, anchor);
            Text.Anchor = wasAnchor;
            Text.WordWrap = wasWrap;
            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h + RowGap;
        }

        float Link(Rect r, float y, string label, string url, GameFont font,
            TextAnchor anchor, int textSize = 0)
        {
            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = font;
            float h = UiTheme.LineHOf(font);
            GUIStyle style = textSize > 0 ? SizedStyle(font, textSize, TextAnchor.UpperLeft,
                false) : null;
            float width = textSize > 0
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
            if (textSize > 0) GUI.Label(hit, label ?? "", style);
            else UiText.RowLabel(hit, label, TextAnchor.UpperLeft);
            if (UiButtons.RowButton(hit))
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
