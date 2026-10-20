using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Coordinates cursor settings, textures and animation.
    [StaticConstructorOnStartup]
    public static class GameCursor
    {
        public sealed class Choice
        {
            public readonly string Key;
            public readonly string Label;
            public readonly string TexturePath;
            public readonly bool Reversed;

            public Choice(string key, string label, string texturePath, bool reversed)
            {
                Key = key;
                Label = label;
                TexturePath = texturePath;
                Reversed = reversed;
            }
        }

        // Mirror selected icons to point toward the top-left.
        static readonly Choice[] _choices =
        {
            new Choice("tame", "Tame", "UI/Designators/Tame", true),
            new Choice("attack", "Attack", "UI/Commands/Attack", true),
            new Choice("extract_skull", "Extract skull", "UI/Designators/ExtractSkull", false),
            new Choice("harvest_wood", "Harvest wood", "UI/Designators/HarvestWood", true),
            new Choice("mine", "Mine", "UI/Designators/Mine", false),
            new Choice("slaughter", "Slaughter", "UI/Designators/Slaughter", false),
            new Choice("attack_melee", "Attack melee", "UI/Commands/AttackMelee", true),
            new Choice("take_drug", "Take drug", "UI/Commands/TakeDrug", true),
        };

        public static Choice[] Choices => _choices;

        public static string CurrentKey =>
            ChoiceFor(Settings.Cursor) != null ? Settings.Cursor : _choices[0].Key;

        static readonly CursorTextures Textures = new CursorTextures();
        static readonly CursorAnimation Animation = new CursorAnimation();

        static GameCursor()
        {
            Apply();
        }

        public static Choice ChoiceFor(string key)
        {
            foreach (var choice in _choices)
                if (choice.Key == key)
                    return choice;
            return null;
        }

        public static string LabelFor(string key)
        {
            var choice = ChoiceFor(key);
            return choice != null ? choice.Label : _choices[0].Label;
        }

        // The picker uses the original asset; CursorTextures builds the cursor copy.
        public static Texture2D Preview(Choice choice) =>
            choice == null ? null : ContentFinder<Texture2D>.Get(choice.TexturePath, false);

        public static void Choose(string key)
        {
            if (ChoiceFor(key) == null)
                key = _choices[0].Key;
            Settings.S.cursor = key;
            Settings.S.Write();
            Apply();
        }

        // Preference changes reapply the cursor. Preserve animation unless textures change.
        public static void Apply()
        {
            EnsureTextures();
            Textures.InvalidateShown();
            Tick();
        }

        static void EnsureTextures()
        {
            var choice = ChoiceFor(Settings.Cursor) ?? _choices[0];
            float brightness = CrtMode.CursorBrightness(Settings.CrtOverlay);
            float opacity = CrtMode.CursorOpacity(Settings.CrtOverlay);
            if (!Textures.Matches(choice, Settings.CursorGrayscale, brightness, opacity))
            {
                Animation.Reset();
                Textures.Build(choice, Settings.CursorGrayscale, brightness, opacity);
            }
        }

        // Ignore repeated pats until the animation ends.
        public static void Pat()
        {
            if (Animation.PatActive)
                return;
            if (!Textures.Ready)
                Apply();
            if (!Textures.Ready)
                return;
            Animation.Pat(Time.realtimeSinceStartup);
        }

        // Animate any mouse-button press.
        public static void Click()
        {
            if (Animation.PatActive)
                return;
            if (!Textures.Ready)
                Apply();
            if (!Textures.Ready)
                return;
            Animation.Click(Time.realtimeSinceStartup);
        }

        public static void Tick()
        {
            EnsureTextures();
            Textures.Show(Animation.Step(Time.realtimeSinceStartup));
        }
    }
}
