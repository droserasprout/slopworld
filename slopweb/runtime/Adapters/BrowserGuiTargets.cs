// Exercise native render-target lifetime and cached sampling without a colony.
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserGuiTargets
{
    static BrowserGui? gui;
    static RenderTexture? target;
    static Texture2D? image;

    public static string Frame(int stage)
    {
        if (stage == 0)
        {
            gui = BrowserGui.Install();
            target = new RenderTexture(32, 32, 0);
            image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            image.SetPixels32(new Color32[] { Color.red });
            image.Apply(false, false);
            if (SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None)
                throw new InvalidOperationException("Browser advertised unsupported GPU copies");
        }
        if (gui == null || target == null)
            throw new InvalidOperationException("Target check not initialized");
        GUI.matrix = Matrix4x4.identity;
        Event.current = new Event { type = EventType.Repaint, rawType = EventType.Repaint };
        if (stage == 2)
        {
            image!.SetPixels32(new Color32[] { Color.green });
            image.Apply(false, false);
            target.Release();
            target.width = target.height = 16;
            target.Create();
        }
        if (stage == 0 || stage == 2)
        {
            RenderTexture.active = target;
            try
            {
                gui.FillRect(
                    new Rect(0, 0, target.width, target.height),
                    stage == 0 ? Color.red : Color.green
                );
                if (stage == 0)
                {
                    GUI.BeginGroup(new Rect(0, 0, 16, 16));
                    gui.FillRect(new Rect(0, 0, 32, 32), Color.blue);
                    GUI.EndGroup();
                }
            }
            finally
            {
                RenderTexture.active = null;
            }
        }
        if (stage < 3)
        {
            GUI.DrawTexture(new Rect(4, 4, 32, 32), target);
            GUI.DrawTexture(new Rect(36, 4, 4, 4), image!);
        }
        else
        {
            target.Release();
            target.Release();
            target.Dispose();
            image!.Dispose();
        }
        return gui.Drain();
    }
}
