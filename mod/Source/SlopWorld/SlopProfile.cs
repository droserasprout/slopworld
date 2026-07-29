using System.IO;
using System.Xml;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // The one question asked before anything else: is this our install, or somebody's
    // game?
    //
    // Every other mod is something you add to a colony. This one takes the sim away,
    // renames the faction, rewrites the calendar, deletes every vanilla song and ships
    // defs a vanilla save has never heard of. Loaded into an ordinary install it does
    // not degrade, it eats the place - and a colony somebody cared about comes back
    // wearing a faceplate, on a map with no rock in it, in a save that no longer loads
    // without us.
    //
    // So there is a profile: a save data folder of our own, handed to the game with
    // `-savedatafolder=` by the `slopworld` runner, holding a mod list that is Core and
    // us. The runner writes a marker file into it, and that file is the whole of this
    // check. It is written by the runner rather than by us on purpose: a folder the
    // game made for itself is an install, and the only way to say "this one is for
    // agents" is for something outside the game to have said so.
    //
    // Refusing means refusing *before touching anything*: no Harmony patches, no stat
    // parts, no XML operations. What is left of us in a vanilla game is the defs we
    // add - four main buttons, which say so when clicked, and some sounds and flecks
    // nothing references. The parts that rewrite vanilla's own defs go through
    // PatchOperationInProfile below and stand down with the rest.
    public static class SlopProfile
    {
        // Written by `slopworld`; see slopd/src/bin/slopworld.rs.
        public const string Marker = "slopworld.profile";

        static bool? _ok;
        static bool _complained;

        /// Where the game is keeping its saves - the profile, if we are in one.
        public static string Folder
        {
            get
            {
                try { return GenFilePaths.SaveDataFolderPath; }
                catch { return ""; }
            }
        }

        // Asked at XML patch time, long before defs exist, and again at every gate
        // afterwards, so it is answered once and remembered. Nothing moves the marker
        // while the game is up.
        public static bool Ok
        {
            get
            {
                if (_ok == null) _ok = Check();
                return _ok.Value;
            }
        }

        static bool Check()
        {
            try
            {
                var folder = GenFilePaths.SaveDataFolderPath;
                return !folder.NullOrEmpty() && File.Exists(Path.Combine(folder, Marker));
            }
            catch (Exception e)
            {
                // Unreadable is not a profile. Refusing is the safe way to be wrong.
                Log.Warning("[SlopWorld] could not read the save data folder: " + e.Message);
                return false;
            }
        }

        // The log line is for whoever is reading Player.log and is said once, since the
        // gates that call this are every door in the mod. The dialog is for whoever is
        // looking at the screen and is said every time one is tried: a button that
        // silently does nothing is worse than the mod being absent.
        //
        // Queued rather than added, because the first caller is a static constructor
        // and there is no window stack yet. With nothing loading, the queue is drained
        // on the next frame anyway.
        public static void Complain()
        {
            if (!_complained)
            {
                _complained = true;
                Log.Error(
                    "[SlopWorld] not a SlopWorld profile, so nothing has been patched. " +
                    "The save data folder is " + Folder + " and it has no " + Marker + " in it. " +
                    "Launch the game with the `slopworld` runner instead.");
            }

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var text =
                    "SlopWorld is loaded, but this is not a SlopWorld profile, so it has " +
                    "patched nothing and your game is untouched.\n\n" +
                    "This mod is not an addition to a colony. It removes the simulation, " +
                    "the songs and the world's mountains, and its saves do not load without " +
                    "it - so it keeps to a save folder of its own.\n\n" +
                    "Launch it with the runner instead:\n\n    slopworld\n\n" +
                    "which makes that folder, enables Core and SlopWorld alone in it, and " +
                    "starts the game there. `make install` puts the runner on your PATH.\n\n" +
                    "This folder: " + Folder;

                Find.WindowStack.Add(new Dialog_MessageBox(
                    text,
                    "Quit", Root.Shutdown,
                    "Close", null,
                    "SlopWorld"));
            });
        }
    }

    // The XML half of standing down. Vanilla's own patch operations cannot ask a
    // question, and ours are the ones that rewrite defs the base game shipped -
    // removing every song, taking the rocks out of map generation, handing the player
    // buildings no player was meant to build. Wrapped in this, they are applied in a
    // profile and skipped everywhere else.
    //
    // Assemblies are loaded before the XML is patched, which is the only reason this
    // can exist; static constructors run after, which is why the C# half is a separate
    // gate rather than this one.
    public class PatchOperationInProfile : PatchOperationSequence
    {
        // True either way: nothing was done, and nothing went wrong. A false here is
        // what makes the game log a failed patch, which is not what a mod standing
        // aside politely should look like.
        protected override bool ApplyWorker(XmlDocument xml) =>
            !SlopProfile.Ok || base.ApplyWorker(xml);
    }
}
