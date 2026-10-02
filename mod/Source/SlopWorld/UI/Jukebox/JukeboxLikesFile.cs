using System;
using System.IO;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Likes live on the game host; open its local file association rather than a daemon editor.
    public static class JukeboxLikesFile
    {
        public static void Edit()
        {
            try
            {
                string path = Radio.LikesPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) File.WriteAllText(path, "");
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
