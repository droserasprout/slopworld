using System;
namespace SlopWorld
{
    static class HubWire
    {
        public static string Esc(string name) => Uri.EscapeDataString(name ?? "");
    }
}
