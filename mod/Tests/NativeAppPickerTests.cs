using System.IO;
using NUnit.Framework;
using static SlopWorld.Tests.ViewCommandFixture;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeAppPickerTests
    {
        [TestCase(2, 1)]
        [TestCase(3, 0)]
        public void PortalFallbackRequiresChooserSupportAndPassesTheFileDescriptor(int version, int exitCode)
        {
            string setup = Path.GetTempFileName();
            string selected = Path.GetTempFileName();
            try
            {
                File.WriteAllText(selected, "selected file");
                // Stub host commands in each noninteractive bash, including the login shell.
                // This exercises the generated command without opening any desktop UI.
                File.WriteAllText(setup, "python3() { return 1; }\n" +
                    "gdbus() { case \"$*\" in *Properties.Get*) printf '(<uint32 " + version +
                    ">,)\\n';; *) cat <&3;; esac; }\n");
                string output = Run(NativeAppPicker.Command(selected), exitCode, setup);
                Assert.That(output, Is.EqualTo(version >= 3 ? "selected file" : ""));
            }
            finally { File.Delete(setup); File.Delete(selected); }
        }

    }
}
