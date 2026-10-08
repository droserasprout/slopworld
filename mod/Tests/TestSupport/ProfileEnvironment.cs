using System;
using System.Collections.Generic;
using System.Xml;

namespace Verse
{
    public class Window { public bool closeOnCancel = true; }
    public class WindowStack : List<object>
    {
        public bool IsOpen(Window window) => Contains(window);
        public void Add(Window window) => base.Add(window);
    }
    public static class ModsConfig
    {
        public static readonly HashSet<string> Active = new HashSet<string>();
        public static int Saves;
        public static bool FailSave;
        public static void SetActive(string id, bool active)
        {
            if (active) Active.Add(id);
            else Active.Remove(id);
        }
        public static void Save()
        {
            Saves++;
            if (FailSave) throw new System.IO.IOException("test save failure");
        }
    }
    public class PatchOperationSequence
    {
        protected virtual bool ApplyWorker(XmlDocument xml) => true;
    }
}
namespace SlopWorld
{
    static class AlertDialog
    {
        public static Verse.Window Create(string title, string text, string primary,
            Action action, string secondary = null, Action secondaryAction = null, UiTheme.Btn primaryKind = UiTheme.Btn.Primary) => new Verse.Window();
    }
}
