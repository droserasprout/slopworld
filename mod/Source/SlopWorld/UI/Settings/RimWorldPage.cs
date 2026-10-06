using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace SlopWorld
{
    // Owns vanilla options rendering and its retained dialog, links, and scroll state.
    public sealed class RimWorldPage : IOptionPage
    {
        const GameFont RegularFont = GameFont.Medium;
        const float ContentPaddingX = 20f;
        const float ContentPaddingY = 16f;
        const int BodyTextSize = 15;
        static float HeroMargin => UiTheme.GapS;
        static float HeadingGap => UiTheme.GapM;
        static float ColumnGap => UiTheme.GapL;
        readonly SmoothScroll _rimWorldScroll = new SmoothScroll();
        readonly Dialog_Options _rimWorldOptions = new Dialog_Options();
        readonly ContentHeight _rimWorldHeight = new ContentHeight(1800f);
        List<ListableOption> _links;
        public void Load() { }

        const string EulaDisclaimer =
            "Portions of the materials used to create this content/mod are trademarks and/or " +
            "copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This " +
            "content/mod is not official and is not endorsed by Ludeon.";

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.BodyWithoutFooter(rect);

            var view = SettingsPageLayout.ScrollView(inner,
                _rimWorldHeight.BeginFrame(Time.frameCount),
                ContentPaddingX, ContentPaddingY, out var content);

            using (_rimWorldScroll.Scope(inner, view))
            {
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
                    (NamedArgument)VersionControl.CurrentBuildDate.ToString("MMM d yyyy", CultureInfo.CurrentCulture)));
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

        float DrawEulaDisclaimer(Rect r, float y)
        {
            y += HeroMargin;
            y = UiText.Paragraph(r, y, EulaDisclaimer, RegularFont, UiTheme.Dim,
                TextAnchor.UpperCenter, BodyTextSize);
            return y + HeroMargin;
        }


    }
}
