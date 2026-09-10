using System.IO;
using System.Xml;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // The runner marks its own `-savedatafolder=`. Refuse unmarked installs before Harmony,
    // stat or XML work; this mod replaces the sim and adds defs, faction/calendar changes and
    // music changes that must not touch a vanilla game.
    public static class ModProfile
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

        // Asked at XML patch time, before defs exist, and again at every gate afterwards, so
        // it is answered once and remembered.
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

        // The log line once, the dialog every time a door is tried: a button that silently
        // does nothing is worse than the mod being absent. Queued rather than added, the first
        // caller being a static constructor with no window stack yet.
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

                Find.WindowStack.Add(AlertDialog.Create(
                    "SlopWorld", text, "Quit", Root.Shutdown, "Close", null,
                    UiTheme.Btn.Danger));
            });
        }
    }

    // The XML half of standing down: wraps every operation of ours that rewrites a def the
    // base game shipped. Assemblies load before the XML is patched, which is the only reason
    // this can exist; static constructors run after, which is why the C# gate is separate.
    public class PatchOperationInProfile : PatchOperationSequence
    {
        // True either way - a false is what makes the game log a failed patch.
        protected override bool ApplyWorker(XmlDocument xml) =>
            !ModProfile.Ok || base.ApplyWorker(xml);
    }
}
