using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace SlopWorld
{
    // The last page of the options menu: this machine's build of RimWorld, who is
    // logged in, and every link the game ships as a one-click door. What the main
    // menu used to show before this mod took it over.
    //
    // Version info is read from VersionControl, which is the game's own static
    // scribed from the assembly version. The new-version notification comes from
    // LatestVersionGetter, a MonoBehaviour on the root object that fetches
    // rimworldgame.com once per process.
    public class AboutPage
    {
        // Built once per open, because the list of expansions is stable.
        List<ListableOption> _links;

        public void Draw(Rect rect)
        {
            // Split the page: version info on the left, web links on the right.
            // The links want a narrower column and the info wants width for the
            // version string and the Steam name.
            float rightW = Mathf.Min(145f + 17f, rect.width * 0.4f);
            float leftW = rect.width - rightW - 12f;
            var left = new Rect(rect.x, rect.y, leftW, rect.height);
            var right = new Rect(rect.xMax - rightW, rect.y, rightW, rect.height);

            DrawVersionInfo(left);
            DrawWebLinks(right);
        }

        void DrawVersionInfo(Rect r)
        {
            float y = r.y;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(r.x, y, r.width, 22f), "RimWorld build");
            GUI.color = Color.white;
            y += 24f;

            // Version: "VersionIndicator" e.g. "RimWorld 1.6.4825 rev123"
            var label = "VersionIndicator".Translate(
                (NamedArgument)VersionControl.CurrentVersionString);
            Widgets.Label(new Rect(r.x, y, r.width, 24f), label);
            y += 24f;

            // Build date: "CompiledOn" e.g. "Compiled on Jan 15 2025"
            label = "CompiledOn".Translate(
                (NamedArgument)VersionControl.CurrentBuildDate
                    .ToString("MMM d yyyy"));
            Widgets.Label(new Rect(r.x, y, r.width, 24f), label);
            y += 24f;

            // Steam persona name, if logged in
            if (SteamManager.Initialized)
            {
                y += 4f;
                label = "LoggedIntoSteamAs".Translate(
                    (NamedArgument)SteamUtility.SteamPersonaName);
                Widgets.Label(new Rect(r.x, y, r.width, 24f), label);
                y += 24f;
            }

            y += 8f;

            // New version notification, from the root object's component.
            // DrawAt paints directly to the screen, so we give it the real rect.
            var lvg = Current.Root?.gameObject
                .GetComponent<LatestVersionGetter>();
            if (lvg != null)
            {
                var note = new Rect(r.x, y, r.width, 50f);
                lvg.DrawAt(note);
                y += 54f;
            }

            y += 12f;

            // Expansion icons, reproduced from MainMenuDrawer.DoExpansionIcons
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(r.x, y, r.width, 22f), "Expansions");
            GUI.color = Color.white;
            y += 24f;

            var exps = ModLister.AllExpansions;
            float x = r.x;
            for (int i = 0; i < exps.Count; i++)
            {
                if (exps[i].isCore) continue;
                var icon = exps[i].Icon;
                if (icon == null) continue;
                var irect = new Rect(x, y, 64f, 64f);
                GUI.DrawTexture(irect, icon);
                TooltipHandler.TipRegion(irect, exps[i].label);
                x += 64f + 8f;
            }
        }

        void DrawWebLinks(Rect r)
        {
            if (_links == null)
                _links = BuildLinks();

            Widgets.DrawMenuSection(r);
            var inner = r.ContractedBy(8f);
            // The links are drawn with the same OptionListingUtility the main
            // menu uses, so they look and behave the same way.
            float used = OptionListingUtility.DrawOptionListing(inner, _links);
            // If the links filled the column, nothing more to do.
            if (used >= inner.height) return;

            // The language selector, below the web links.
            float top = inner.y + used + 8f;
            if (Widgets.ButtonText(new Rect(inner.x, top, inner.width, 30f),
                    LanguageDatabase.activeLanguage.FriendlyNameNative))
            {
                var opts = new List<FloatMenuOption>();
                foreach (var lang in LanguageDatabase.AllLoadedLanguages)
                {
                    var local = lang;
                    opts.Add(new FloatMenuOption(local.DisplayName,
                        () =>
                        {
                            LanguageDatabase.SelectLanguage(local);
                            Prefs.Save();
                        }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        static List<ListableOption> BuildLinks()
        {
            var list = new List<ListableOption>();

            // The same links the main menu draws, in the same order.
            list.Add(new ListableOption_WebLink(
                "FictionPrimer".Translate(),
                "https://rimworldgame.com/backstory",
                TexButton.IconBlog));

            list.Add(new ListableOption_WebLink(
                "LudeonBlog".Translate(),
                "https://ludeon.com/blog",
                TexButton.IconBlog));

            list.Add(new ListableOption_WebLink(
                "Subreddit".Translate(),
                "https://www.reddit.com/r/RimWorld/",
                TexButton.IconReddit));

            list.Add(new ListableOption_WebLink(
                "OfficialWiki".Translate(),
                "https://rimworldwiki.com",
                TexButton.IconWiki));

            list.Add(new ListableOption_WebLink(
                "TynansX".Translate(),
                "https://x.com/TynanSylvester",
                TexButton.IconX));

            list.Add(new ListableOption_WebLink(
                "TynansDesignBook".Translate(),
                "https://tynansylvester.com/book",
                TexButton.IconBook));

            list.Add(new ListableOption_WebLink(
                "HelpTranslate".Translate(),
                "https://rimworldgame.com/helptranslate",
                TexButton.IconForums));

            // Buy soundtrack opens a submenu, same as the main menu.
            list.Add(new ListableOption_WebLink(
                "BuySoundtrack".Translate(),
                () =>
                {
                    var opts = new List<FloatMenuOption>();
                    opts.Add(new FloatMenuOption(
                        "BuySoundtrack_Classic".Translate(),
                        () => Application.OpenURL(
                            "https://store.steampowered.com/app/990430/RimWorld_Soundtrack/")));
                    opts.Add(new FloatMenuOption(
                        "BuySoundtrack_Royalty".Translate(),
                        () => Application.OpenURL(
                            "https://store.steampowered.com/app/1244270/RimWorld_Royalty_Soundtrack/")));
                    opts.Add(new FloatMenuOption(
                        "BuySoundtrack_Anomaly".Translate(),
                        () => Application.OpenURL(
                            "https://store.steampowered.com/app/2914900/RimWorld_Anomaly_Soundtrack/")));
                    opts.Add(new FloatMenuOption(
                        "BuySoundtrack_Odyssey".Translate(),
                        () => Application.OpenURL(
                            "https://store.steampowered.com/app/3689230/RimWorld_Odyssey_Soundtrack/")));
                    Find.WindowStack.Add(new FloatMenu(opts));
                },
                TexButton.IconSoundtrack));

            return list;
        }
    }
}