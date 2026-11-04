using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlopWorld
{
    // Linux font discovery order only. Unity owns rendering; terminal policy owns caching.
    static class FontconfigFonts
    {
        const string Library = "libfontconfig.so.1";
        static readonly byte[] FamilyProperty = Encoding.ASCII.GetBytes("family\0");
        static readonly byte[] ColorProperty = Encoding.ASCII.GetBytes("color\0");

        public static string[] Families(string family)
        {
            if (FcInitBringUptoDate() == 0)
                throw new InvalidOperationException("Fontconfig initialization failed");
            IntPtr pattern = FcPatternCreate();
            if (pattern == IntPtr.Zero)
                throw new InvalidOperationException("Fontconfig pattern allocation failed");
            IntPtr fonts = IntPtr.Zero;
            try
            {
                // Literal family, not pattern syntax. No shell or subprocess involved.
                if (
                    FcPatternAddString(
                        pattern,
                        FamilyProperty,
                        Encoding.UTF8.GetBytes(family + "\0")
                    ) == 0
                    || FcConfigSubstitute(IntPtr.Zero, pattern, 0) == 0
                )
                    throw new InvalidOperationException("Fontconfig substitution failed");
                FcDefaultSubstitute(pattern);
                // Keep coverage duplicates until color faces are filtered, otherwise
                // a color font could hide a later usable monochrome fallback.
                fonts = FcFontSort(IntPtr.Zero, pattern, 0, IntPtr.Zero, out int result);
                if (fonts == IntPtr.Zero || result != 0)
                    throw new InvalidOperationException("Fontconfig font sorting failed");
                var set = Marshal.PtrToStructure<FontSet>(fonts);
                var names = new List<string>();
                for (int i = 0; i < set.Count; i++)
                {
                    IntPtr face = Marshal.ReadIntPtr(set.Fonts, i * IntPtr.Size);
                    // Unity's legacy font path cannot render color emoji fonts.
                    if (FcPatternGetBool(face, ColorProperty, 0, out int color) == 0 && color != 0)
                        continue;
                    if (FcPatternGetString(face, FamilyProperty, 0, out IntPtr value) == 0)
                        names.Add(Utf8(value));
                }
                return names.ToArray();
            }
            finally
            {
                if (fonts != IntPtr.Zero)
                    FcFontSetDestroy(fonts);
                FcPatternDestroy(pattern);
            }
        }

        static string Utf8(IntPtr value)
        {
            int length = 0;
            while (Marshal.ReadByte(value, length) != 0)
                length++;
            var bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FontSet
        {
            public int Count;
            public int Capacity;
            public IntPtr Fonts;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int FcInitBringUptoDate();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FcPatternCreate();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int FcPatternAddString(IntPtr pattern, byte[] name, byte[] value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int FcConfigSubstitute(IntPtr config, IntPtr pattern, int kind);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void FcDefaultSubstitute(IntPtr pattern);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FcFontSort(
            IntPtr config,
            IntPtr pattern,
            int trim,
            IntPtr charset,
            out int result
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int FcPatternGetString(
            IntPtr pattern,
            byte[] name,
            int index,
            out IntPtr value
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int FcPatternGetBool(IntPtr pattern, byte[] name, int index, out int value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void FcFontSetDestroy(IntPtr fonts);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void FcPatternDestroy(IntPtr pattern);
    }
}
