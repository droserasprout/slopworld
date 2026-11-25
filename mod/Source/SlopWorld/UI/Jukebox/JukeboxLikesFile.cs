using System;
using System.IO;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Each host edits the same document its Like action writes.
    public static class JukeboxLikesFile
    {
        public static void Edit()
        {
            try
            {
                if (JukeboxLibrary.BrowserRead != null)
                {
                    WorkspaceWindow.OpenOverPane(new JukeboxLikesEditor(JukeboxLibrary.Read()));
                    return;
                }
                string path = ApplicationPaths.LikesPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path))
                    File.WriteAllText(path, "");
                Application.OpenURL(new Uri(path).AbsoluteUri);
            }
            catch (Exception error)
            {
                Log.Error("[SlopWorld] jukebox: could not open liked songs: " + error);
                UiLayout.Fail("could not open liked songs");
            }
        }
    }
}
