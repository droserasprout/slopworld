using UnityEngine;

namespace SlopWorld
{
    // An options page is built lazily, loaded once, then handed its rect. Config-backed pages
    // use Load to read their source, while local pages can make it a no-op. The shared shape
    // lets ModOptions dispatch every category from the same tab table.
    public interface IOptionPage
    {
        void Load();
        void Draw(Rect rect);
    }
}
