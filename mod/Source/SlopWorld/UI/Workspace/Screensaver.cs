using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns temporary UI suppression; windows and workspace panels retain their lifetimes.
    // MenuBackground owns animation frames and MenuBackgroundLayers owns their rendering.
    internal static class Screensaver
    {
        static Game _game;
        static int _enteredFrame;
        static int _wakeFrame;

        internal static void Enter()
        {
            _game = Current.Game;
            _enteredFrame = Time.frameCount;
            _wakeFrame = -1;
        }

        internal static bool HandleGUI()
        {
            if (_game == null) return false;
            if (Current.ProgramState != ProgramState.Playing || Current.Game != _game ||
                LongEventHandler.ShouldWaitForEvent)
            {
                _game = null;
                return false;
            }
            if (_wakeFrame >= 0 && Time.frameCount != _wakeFrame)
            {
                _game = null;
                return false;
            }

            var e = Event.current;
            // The palette's Enter may also emit a character event in the activation frame.
            // Suppress the whole wake frame so its character event cannot reach a terminal,
            // shortcut, or map after the physical key event has been consumed.
            if (e.type == EventType.KeyDown && Time.frameCount != _enteredFrame)
                _wakeFrame = Time.frameCount;

            if (e.type == EventType.Repaint) Draw();
            else if (e.isKey || e.isMouse || e.type == EventType.ScrollWheel) e.Use();
            return true;
        }

        static void Draw()
        {
            // Root.OnGUI normally establishes these before dispatching any UI drawing.
            GUI.depth = 50;
            UI.ApplyUIScale();
            var rect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            var saved = GUI.color;
            try
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, BaseContent.BlackTex);
                var source = MenuBackground.HasFrames ? null :
                    ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);
                var frame = MenuBackground.Current(source);
                if (frame == null) return;
                float brightness = 1f - Mathf.Clamp01(Settings.EcoDim);
                GUI.color = new Color(brightness, brightness, brightness, 1f);
                MenuBackgroundLayers.DrawLayers(rect, frame, ScaleMode.ScaleAndCrop);
            }
            finally
            {
                GUI.color = saved;
            }
        }
    }
}
