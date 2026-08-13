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
        public float ContentPaddingY = 28f;
        public float HeroMargin = 24f;
        public float SectionMargin = 22f;
        public float SectionPadding = 20f;
        public float ColumnGap = SlopWidgets.GapL;
        public float ColumnPadding = 10f;
        public float ColumnHeadingPadding = 16f;
        public float CreditMargin = 18f;
        public float LinkMargin = 6f;
        public float ParagraphBottomPadding = 3f;
        public float TailPadding = 120f;
        public int KickerTextSize;
        public int TitleTextSize = 28;
        public int SubtitleTextSize = 17;
        public int SectionTextSize = 21;
        public int ColumnHeadingTextSize = 17;
        public int BodyTextSize;
        public int CreditTextSize;
        public int LinkTextSize;
    }

    // Temporary credits-style page with local primitives that can move to shared UI later.
    public class AboutPage
    {
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
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit.");

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

            float used = OptionListingUtility.DrawOptionListing(
                new Rect(r.x, y, r.width, 1000f), _links);
            y += used + 8f;

            if (SlopWidgets.Button(new Rect(r.x, y, r.width, SlopWidgets.BtnH),
                    LanguageDatabase.activeLanguage.FriendlyNameNative))
            {
                var opts = new List<FloatMenuOption>();
                foreach (var lang in LanguageDatabase.AllLoadedLanguages)
                {
                    var local = lang;
                    opts.Add(new FloatMenuOption(local.DisplayName, () =>
                    {
                        LanguageDatabase.SelectLanguage(local);
                        Prefs.Save();
                    }));
                }
                Find.WindowStack.Add(new SlopMenu(opts));
            }
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

            y = Line(r, y, "LOREM IPSUM", GameFont.Tiny,
                SlopWidgets.Faint, TextAnchor.UpperCenter, Layout.KickerTextSize);
            y += SlopWidgets.GapS;
            y = Line(r, y, "DOLOR SIT AMET", GameFont.Medium,
                SlopWidgets.Lead, TextAnchor.UpperCenter, Layout.TitleTextSize);
            y = Line(r, y, "CONSECTETUR ADIPISCING ELIT", GameFont.Small,
                SlopWidgets.Dim, TextAnchor.UpperCenter, Layout.SubtitleTextSize);
            y += Layout.HeroMargin;

            y = Paragraph(r, y,
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris " +
                "nisi ut aliquip ex ea commodo consequat.",
                GameFont.Small, SlopWidgets.Name, TextAnchor.UpperCenter,
                Layout.BodyTextSize);
            y += Layout.SectionMargin;
            Rule(r, y);
            y += Layout.SectionPadding;

            y = Line(r, y, "LOREM IPSUM DOLOR", GameFont.Tiny,
                SlopWidgets.Faint, TextAnchor.UpperCenter, Layout.KickerTextSize);
            y += Layout.ColumnPadding;

            float gap = Layout.ColumnGap;
            float columnWidth = Mathf.Max(1f, (r.width - gap) / 2f);
            var left = new Rect(r.x, y, columnWidth, 400f);
            var right = new Rect(r.x + columnWidth + gap, y, columnWidth, 400f);
            float leftHeight = CreditsColumn(left, TextAnchor.UpperRight,
                "LOREM IPSUM", new[]
                {
                    "Lorem ipsum", "Dolor sit amet", "Consectetur adipiscing",
                    "Sed do eiusmod"
                }, new[]
                {
                    "tempor incididunt", "ut labore et dolore", "magna aliqua", "quis nostrud"
                });
            float rightHeight = CreditsColumn(right, TextAnchor.UpperLeft,
                "DOLOR SIT AMET", new[]
                {
                    "Exercitation ullamco", "Laboris nisi", "Ut aliquip ex ea",
                    "Commodo consequat"
                }, new[]
                {
                    "Duis aute irure", "Dolor in reprehenderit", "Voluptate velit",
                    "Esse cillum dolore"
                });
            y += Mathf.Max(leftHeight, rightHeight) + Layout.SectionMargin;

            Rule(r, y);
            y += Layout.SectionPadding;
            y = Line(r, y, "CONSECTETUR ADIPISCING", GameFont.Medium,
                SlopWidgets.Lead, TextAnchor.UpperCenter, Layout.SectionTextSize);
            y += Layout.ColumnHeadingPadding;

            gap = Layout.ColumnGap;
            columnWidth = Mathf.Max(1f, (r.width - gap * 2f) / 3f);
            var castLeft = new Rect(r.x, y, columnWidth, 420f);
            var castMiddle = new Rect(r.x + columnWidth + gap, y, columnWidth, 420f);
            var castRight = new Rect(r.x + (columnWidth + gap) * 2f, y, columnWidth, 420f);
            float castLeftHeight = CreditsColumn(castLeft, TextAnchor.UpperLeft,
                "LOREM", new[]
                {
                    "Lorem ipsum", "Dolor sit amet", "Consectetur elit",
                    "Sed eiusmod"
                }, new[]
                {
                    "Adipiscing", "Tempor incididunt", "Labore et dolore", "Magna aliqua"
                });
            float castMiddleHeight = CreditsColumn(castMiddle, TextAnchor.UpperCenter,
                "IPSUM", new[]
                {
                    "Ut enim ad", "Minim veniam", "Quis nostrud", "Exercitation"
                }, new[]
                {
                    "Ullamco laboris", "Nisi ut aliquip", "Ex ea commodo", "Consequat duis"
                });
            float castRightHeight = CreditsColumn(castRight, TextAnchor.UpperRight,
                "DOLOR", new[]
                {
                    "Aute irure", "Dolor reprehenderit", "Voluptate velit",
                    "Esse cillum"
                }, new[]
                {
                    "Fugiat nulla", "Pariatur excepteur", "Sint occaecat", "Cupidatat"
                });
            y += Mathf.Max(castLeftHeight, Mathf.Max(castMiddleHeight, castRightHeight)) +
                Layout.SectionMargin;

            Rule(r, y);
            y += Layout.SectionMargin;
            y = Paragraph(r, y,
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                "Suspendisse potenti. Integer at sem sed nulla commodo consequat. " +
                "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum " +
                "dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non " +
                "proident, sunt in culpa qui officia deserunt mollit anim id est laborum.",
                GameFont.Small, SlopWidgets.Dim, TextAnchor.UpperLeft,
                Layout.BodyTextSize);
            y += Layout.SectionMargin;

            y = Line(r, y, "LOREM IPSUM", GameFont.Small,
                SlopWidgets.Lead, TextAnchor.UpperCenter, Layout.SectionTextSize);
            y += Layout.ColumnPadding;
            y = Link(r, y, "DOLOR SIT AMET", "https://rimworldgame.com/backstory",
                GameFont.Small, TextAnchor.UpperLeft, Layout.LinkTextSize);
            y = Link(r, y, "CONSECTETUR", "https://ludeon.com/blog",
                GameFont.Small, TextAnchor.UpperCenter, Layout.LinkTextSize);
            y = Link(r, y, "ADIPISCING ELIT", "https://rimworldwiki.com",
                GameFont.Small, TextAnchor.UpperRight, Layout.LinkTextSize);
            y += Layout.SectionMargin;

            Rule(r, y);
            y += Layout.SectionMargin;
            y = Line(r, y, "LOREM IPSUM DOLOR SIT AMET", GameFont.Medium,
                SlopWidgets.Lead, TextAnchor.UpperCenter, Layout.SectionTextSize);
            y += Layout.ColumnHeadingPadding;
            y = Paragraph(r, y,
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod " +
                "tempor incididunt ut labore et dolore magna aliqua.",
                GameFont.Small, SlopWidgets.Name, TextAnchor.UpperCenter,
                Layout.BodyTextSize);

            // Leave a tail after the final line so it can reach the bottom of the viewport.
            return y + Layout.TailPadding;
        }

        float CreditsColumn(Rect rect, TextAnchor anchor, string heading,
            string[] names, string[] roles)
        {
            float top = rect.y;
            float y = top;
            y = Line(rect, y, heading, GameFont.Small, SlopWidgets.Lead, anchor,
                Layout.ColumnHeadingTextSize);
            y += 12f;

            for (int i = 0; i < names.Length; i++)
            {
                y = Line(rect, y, names[i], GameFont.Small, SlopWidgets.Name, anchor,
                    Layout.CreditTextSize);
                y = Line(rect, y, roles[i], GameFont.Tiny, SlopWidgets.Dim, anchor,
                    Layout.KickerTextSize);
                y += Layout.CreditMargin;
            }

            return y - top;
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
                    Layout.ParagraphBottomPadding;
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
                Layout.ParagraphBottomPadding;
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
            Slab.Hairline(new Rect(hit.x, hit.yMax - 1f, hit.width, 1f), GUI.color);

            if (Widgets.ButtonInvisible(hit))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Application.OpenURL(url);
            }

            GUI.color = wasColor;
            Text.Font = wasFont;
            return y + h + Layout.LinkMargin;
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

        static void Rule(Rect r, float y) =>
            Slab.Hairline(new Rect(r.x, y, r.width, 1f), SlopWidgets.Edge);
    }
}
