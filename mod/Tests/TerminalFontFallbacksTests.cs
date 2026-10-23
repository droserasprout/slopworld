using System;

namespace SlopWorld.Tests
{
    public static class TerminalFontFallbacksTests
    {
        public static void InstalledVariantsPreserveFamilyAndWidthPriorities()
        {
            var chain = TerminalFontFallbacks.Build(
                new[] { "Fira Code", "JetBrains Mono", "Hack" },
                new[]
                {
                    "Hack Nerd Font Mono",
                    "FiraCode Nerd Font",
                    "FiraCode Nerd Font Propo",
                    "JetBrainsMono Nerd Font",
                    "FiraCode Nerd Font Mono",
                    "firacode nerd font mono",
                    "FiraCodeExtra Nerd Font Mono",
                }
            );
            AssertEx.Equal(
                "FiraCode Nerd Font Mono|FiraCode Nerd Font|Fira Code|JetBrainsMono Nerd Font|JetBrains Mono|Hack Nerd Font Mono|Hack",
                string.Join("|", chain),
                "Nerd variants precede originals within each family, deduplicated and without proportional variants"
            );
        }

        public static void RenamedFamiliesResolveWithoutExplicitVariantLists()
        {
            var chain = TerminalFontFallbacks.Build(
                new[] { "Cascadia Code", "Source Code Pro", "Noto Sans Mono", "Liberation Mono" },
                new[]
                {
                    "LiterationMono Nerd Font",
                    "NotoSansM Nerd Font Mono",
                    "SauceCodePro Nerd Font",
                    "CaskaydiaCove Nerd Font Mono",
                }
            );
            AssertEx.Equal(
                "CaskaydiaCove Nerd Font Mono|Cascadia Code|SauceCodePro Nerd Font|Source Code Pro|NotoSansM Nerd Font Mono|Noto Sans Mono|LiterationMono Nerd Font|Liberation Mono",
                string.Join("|", chain),
                "reserved and shortened names retain their original family priority"
            );
        }

        public static void ExplicitSelectionWinsAndMissingCatalogPreservesFallbacks()
        {
            AssertEx.Equal(
                "Fira Code|FiraCode Nerd Font Mono",
                string.Join(
                    "|",
                    TerminalFontFallbacks.Build(
                        new[] { "Fira Code" },
                        new[] { "FiraCode Nerd Font Mono" },
                        "Fira Code"
                    )
                ),
                "explicit original selection precedes its Nerd Font variant"
            );
            var chain = TerminalFontFallbacks.Build(
                new[] { "Fira Code", "Hack" },
                new[] { "Hack Nerd Font Mono" },
                "Hack Nerd Font Mono"
            );
            AssertEx.Equal(
                "Hack Nerd Font Mono|Fira Code|Hack",
                string.Join("|", chain),
                "explicit selections remain first without duplicates"
            );
            AssertEx.Equal(
                "Fira Code|Hack",
                string.Join(
                    "|",
                    TerminalFontFallbacks.Build(
                        new[] { "Fira Code", "Hack" },
                        Array.Empty<string>()
                    )
                ),
                "font discovery failure retains the unpatched fallback chain"
            );
        }
    }
}
