using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace SlopWorld
{
    // The About page is intentionally short. Third-party acknowledgements belong in the
    // release notices, not in the game UI; the separate RimWorld page keeps the vanilla options
    // that share this helper.
    public sealed class AboutPage : IOptionPage
    {
        const float ContentPaddingX = 20f;
        const float ContentPaddingY = 16f;
        const float ColumnGap = SlopWidgets.GapL;
        const float HeadingGap = SlopWidgets.GapM;

        readonly SmoothScroll _rimWorldScroll = new SmoothScroll();
        readonly Dialog_Options _rimWorldOptions = new Dialog_Options();
        float _rimWorldContentHeight;
        List<ListableOption> _links;

        public void Load() { }

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var body = SlopWidgets.PageBody(rect);
            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(body.ContractedBy(SlopWidgets.GapM));
            SlopWidgets.SectionHeading(listing, "SlopWorld");
            listing.Label("RimWorld with the colony sim replaced by live AI coding agents.");
            listing.End();
        }

        public void DrawRimWorld(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = rect.ContractedBy(SlopWidgets.GapM, SlopWidgets.GapM);

            float viewWidth = Mathf.Max(1f, inner.width - SlopWidgets.ScrollbarW);
            float viewHeight = Mathf.Max(inner.height,
                _rimWorldContentHeight > 0f ? _rimWorldContentHeight : 1800f);
            var view = new Rect(0f, 0f, viewWidth, viewHeight);

            _rimWorldScroll.Begin(inner, view);
            var content = new Rect(ContentPaddingX, ContentPaddingY,
                Mathf.Max(1f, view.width - ContentPaddingX * 2f),
                Mathf.Max(1f, view.height - ContentPaddingY * 2f));

            float y = DrawRimWorldHeader(content, content.y);
            y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Graphics,
                "DoVideoOptions");
            y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Interface,
                "DoUIOptions");
            y = DrawRimWorldSection(content, y, OptionCategoryDefOf.Controls,
                "DoControlsOptions");

            _rimWorldContentHeight = y + ContentPaddingY;
            _rimWorldScroll.End();
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

            SlopWidgets.SectionHeading(
                new Rect(rect.x, y, rect.width, SlopWidgets.RowH), category.LabelCap);
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(new Rect(rect.x, y, rect.width, 10000f));
            listing.verticalSpacing = 5f;
            listing.Gap(12f);
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
            float line = SlopWidgets.LineH;
            float step = line + SlopWidgets.GapXS;
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, line), "RimWorld build");
            GUI.color = Color.white;
            y += step;

            SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                "VersionIndicator".Translate(
                    (NamedArgument)VersionControl.CurrentVersionString));
            y += step;
            SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                "CompiledOn".Translate(
                    (NamedArgument)VersionControl.CurrentBuildDate.ToString("MMM d yyyy")));
            y += step;

            if (SteamManager.Initialized)
            {
                y += 4f;
                SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, line),
                    "LoggedIntoSteamAs".Translate(
                        (NamedArgument)SteamUtility.SteamPersonaName));
                y += step;
            }

            y += 8f;
            var lvg = Current.Root?.gameObject.GetComponent<LatestVersionGetter>();
            if (lvg != null)
            {
                lvg.DrawAt(new Rect(rect.x, y, rect.width, 50f));
                y += 54f;
            }

            return y + 12f;
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
                        Find.WindowStack.Add(new SlopMenu(opts));
                    },
                    TexButton.IconSoundtrack),
            };
        }
    }
}
