namespace SlopWorld.Tests
{
    static class FontFamilyIdentityTests
    {
        public static void PatchedAndOriginalNamesShareOneMenuGroup()
        {
            string[] names =
            {
                "Fira Code",
                "Fira Mono",
                "FiraCode Nerd Font",
                "FiraCode Nerd Font Mono",
                "JetBrains Mono",
                "JetBrainsMono Nerd Font",
                "Cascadia Code",
                "CaskaydiaCove Nerd Font Mono",
            };
            AssertEx.Equal(
                "Fira",
                FontFamilyIdentity.MenuGroup(names[2], names),
                "patched Fira shares original group"
            );
            AssertEx.Equal(
                "Fira",
                FontFamilyIdentity.MenuGroup(names[3], names),
                "mono variant shares original group"
            );
            AssertEx.Equal(
                "JetBrains",
                FontFamilyIdentity.MenuGroup(names[5], names),
                "original capitalization and spelling retained"
            );
            AssertEx.Equal(
                "Cascadia",
                FontFamilyIdentity.MenuGroup(names[7], names),
                "reserved-name replacement shares original group"
            );
        }

        public static void UnknownPatchedFamiliesResolveFromInstalledOriginals()
        {
            string[] names = { "Example Coding Mono", "ExampleCodingMono Nerd Font Mono" };
            AssertEx.Equal(
                "Example",
                FontFamilyIdentity.MenuGroup(names[1], names),
                "new families need no hardcoded menu alias"
            );
            AssertEx.Equal(
                "Standalone",
                FontFamilyIdentity.MenuGroup("Standalone Nerd Font Mono", names),
                "patched-only families keep a clean group"
            );
            AssertEx.Equal(
                "Noto",
                FontFamilyIdentity.MenuGroup("Noto Sans", names),
                "unpatched grouping preserved"
            );
        }
    }
}
