using System.IO;
using System.Xml;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // The launcher creates a marker in its save data folder.
    // Require this marker before applying Harmony, stat, or XML changes.
    // Simulation, definition, faction, calendar, and music changes must not affect an ordinary game profile.
    public static class ModProfile
    {
        // Written by `slopworld`. See slopd/src/bin/slopworld.rs.
        public const string Marker = "slopworld.profile";

        static bool? _ok;
        static bool _complained;

        /// The current game save data folder.
        public static string Folder
        {
            get
            {
                try { return GenFilePaths.SaveDataFolderPath; }
                catch { return ""; }
            }
        }

        // Cache the profile check for XML patches and later activation checks.
        // XML patching occurs before definitions exist.
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
                // Reject the profile if SlopWorld cannot read the save data folder.
                Log.Warning("[SlopWorld] could not read the save data folder: " + e.Message);
                return false;
            }
        }

        // Log the error once and show a dialog for each rejected activation attempt.
        // Queue the dialog because the first caller can be a static constructor before the window stack exists.
        public static void Complain()
        {
            if (!_complained)
            {
                _complained = true;
                Log.Error(
                    "[SlopWorld] this profile has no SlopWorld changes. " +
                    "The save data folder " + Folder + " does not contain " + Marker + ". " +
                    "Start the game with the `slopworld` launcher.");
            }

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var text =
                    "The game loaded SlopWorld, but this profile lacks the required marker file. " +
                    "SlopWorld has made no changes to your game.\n\n" +
                    "SlopWorld uses a separate save folder. It removes the colony simulation, " +
                    "the base game music, and the world's mountains. Its saves require SlopWorld.\n\n" +
                    "Start the game with this command:\n\n    slopworld\n\n" +
                    "The launcher creates the separate save folder and enables only Core and SlopWorld. " +
                    "It then starts the game with that folder. " +
                    "Run `make install` to add the launcher to your PATH.\n\n" +
                    "Current save folder: " + Folder;

                Find.WindowStack.Add(AlertDialog.Create(
                    "SlopWorld", text, "Quit", Root.Shutdown, "Close", null,
                    UiTheme.Btn.Danger));
            });
        }
    }

    // Apply XML changes to base-game definitions only in a valid profile.
    // Assemblies load before XML patching, so this operation is available during patching.
    // Static constructors run later and require a separate C# activation check.
    public class PatchOperationInProfile : PatchOperationSequence
    {
        // An inactive profile counts as success. Returning false would log a patch failure.
        protected override bool ApplyWorker(XmlDocument xml) =>
            !ModProfile.Ok || base.ApplyWorker(xml);
    }
}
