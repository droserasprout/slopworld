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
        static bool _noticePending;
        static Window _notice;
        static bool _disabled;

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
                return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, Marker));
            }
            catch (Exception e)
            {
                // Reject the profile if SlopWorld cannot read the save data folder.
                Log.Warning("[SlopWorld] could not read the save data folder: " + e.Message);
                return false;
            }
        }

        // Root.Update retries delivery until the menu's window stack exists. Finishing
        // XML/static initialization does not imply that the root UI is ready.
        public static void Complain()
        {
            if (!_complained)
            {
                _complained = true;
                try
                {
                    ModsConfig.SetActive("io.drsr.slopworld", false);
                    ModsConfig.Save();
                    _disabled = true;
                }
                catch (Exception e)
                {
                    Log.Error("[SlopWorld] could not save automatic mod disabling: " + e);
                }
                Log.Error(
                    "[SlopWorld] refused colony access. " +
                    "The save data folder " + Folder + " does not contain " + Marker + ". " +
                    "Start the game with the `slopworld` launcher.");
            }
            _noticePending = true;
        }

        internal static void ShowPendingNotice()
        {
            if (!_noticePending) return;
            var stack = Find.WindowStack;
            if (stack == null) return;
            if (_notice != null && stack.IsOpen(_notice)) return;

            var text =
                "SlopWorld cannot run in this RimWorld profile. " +
                "This save folder lacks the required slopworld.profile marker.\n\n" +
                (_disabled
                    ? "SlopWorld has been disabled in this profile's mod list. Quit and restart RimWorld to apply the change.\n\n"
                    : "SlopWorld could not save the change to this profile's mod list. Quit and disable SlopWorld before playing.\n\n") +
                "To use SlopWorld, quit and start it with the slopworld launcher. " +
                "It creates a separate save folder and enables Core and SlopWorld.\n\n" +
                "Current save folder: " + Folder;
            _notice = AlertDialog.Create(
                "SlopWorld", text, "Quit RimWorld", Root.Shutdown,
                primaryKind: UiTheme.Btn.Danger);
            // Disabling changes the next launch only. Loaded definitions cannot be
            // unloaded safely, so this session must end before ordinary play resumes.
            _notice.closeOnCancel = false;
            stack.Add(_notice);
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
