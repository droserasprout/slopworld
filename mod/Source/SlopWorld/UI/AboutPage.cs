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
        // Built once per open, because the list of links is stable.
        List<ListableOption> _links;

        public void Draw(Rect rect)
        {
            // One column, left-aligned: version info on top, the web links below
            // it. The links are plain rows now, not a boxed column on the right.
            float y = DrawVersionInfo(rect, rect.y);
            DrawWebLinks(rect, y);
        }

        float DrawVersionInfo(Rect r, float y)
        {

            Text.Font = GameFont.Small;
            GUI.color = SlopWidgets.Dim;
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

            return y + 12f;
        }

        void DrawWebLinks(Rect r, float y)
        {
            if (_links == null)
                _links = BuildLinks();

            // The links are drawn with the same OptionListingUtility the main
            // menu uses, so they look and behave the same way - plain left-aligned
            // rows, no container around them.
            float used = OptionListingUtility.DrawOptionListing(
                new Rect(r.x, y, r.width, 1000f), _links);
            y += used + 8f;

            // The language selector, below the web links.
            if (Widgets.ButtonText(new Rect(r.x, y, r.width, 30f),
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