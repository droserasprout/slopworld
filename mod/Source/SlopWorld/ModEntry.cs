using System.Reflection;
using Verse;

namespace SlopWorld
{
    public class ModEntry : Mod
    {
        public static readonly string ClientVersion = ReadClientVersion();
        public static ModEntry Instance;
        public readonly ModSettings settings;

        static string ReadClientVersion()
        {
            object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(
                typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes.Length > 0)
            {
                string version = ((AssemblyInformationalVersionAttribute)attributes[0])
                    .InformationalVersion;
                if (!string.IsNullOrEmpty(version)) return version;
            }
            return "0.0.1";
        }

        public ModEntry(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = ModSettings.Load();
        }
    }
}
