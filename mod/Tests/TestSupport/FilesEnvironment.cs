using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Directory tests control checkout identity without the sidebar or catalog polling.
    static partial class SidebarScopes
    {
        internal static readonly List<BrowseScope> TestScopes = new List<BrowseScope>();
        internal static readonly HashSet<string> TestDisabled = new HashSet<string>();
        public static string Key(string key) => key;
        public static BrowseScope Find(string key) => TestScopes.Find(scope => scope.Key == key);
        public static string Directory(string key) => Find(key)?.Path ?? "";
        public static string ProjectName(string key) => Find(key)?.Project ?? key;
        public static bool Enabled(string key) => Find(key) != null && !TestDisabled.Contains(key);
        public static List<BrowseScope> EnabledScopes() => TestScopes.Where(scope => Enabled(scope.Key)).ToList();
        public static IEnumerable<string> GroupKeys => TestScopes.SelectMany(scope => new[] { scope.ProjectKey, scope.Key });
    }
}
