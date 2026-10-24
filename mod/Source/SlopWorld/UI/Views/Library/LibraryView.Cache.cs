using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        static bool _rowsReady;
        static int _projectFilterRevision;
        static (
            HubCatalog Catalog,
            int CatalogRevision,
            int ProjectsRevision,
            int WorktreesRevision,
            int ProjectFilterRevision,
            string Query,
            string Kind
        ) _rowsKey;
        static readonly Dictionary<string, Row> RowsByKey = new Dictionary<string, Row>();

        // Catalog publication and worktree replies replace the model. Geometry, folds and
        // selection stay live; repaint never needs to recreate rows or their action closures.
        static void EnsureRows()
        {
            var hub = SessionHub.Instance;
            var key = (
                hub.Catalog,
                hub.Catalog.Revision,
                hub.ProjectsRevision,
                Worktrees.Revision,
                _projectFilterRevision,
                _query,
                _kind
            );
            if (_rowsReady && _rowsKey.Equals(key))
                return;
            // Release the previous index before allocating replacement rows.
            RowsByKey.Clear();
            // Global definitions stay available while project-specific entries follow
            // the Library's own filter. Builtins remain in their attached menus.
            _items = SessionHub
                .Instance.Library.Where(s =>
                    !s.Builtin && (string.IsNullOrEmpty(s.Project) || PassesProject(s.Project))
                )
                .ToList();
            Templates.Clear();
            foreach (var template in SessionHub.Instance.Templates)
            {
                var row = new LibraryItemInfo
                {
                    Name = template.Name,
                    Project = "",
                    Text = template.Description,
                };
                Templates[row] = template;
                _items.Add(row);
            }
            _items = _items
                .Where(item =>
                    (_kind.Length == 0 || GroupKey(item) == _kind)
                    && (
                        item.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                        || (item.Text ?? "").IndexOf(_query, StringComparison.OrdinalIgnoreCase)
                            >= 0
                    )
                )
                .ToList();
            var projects = SessionHub
                .Instance.Projects.Where(project => PassesProject(project.Name))
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (MainCategoryEnabled(Category.Worktrees))
                Worktrees.Update(projects, SessionHub.Instance.ProjectsRevision);
            BuildSections(projects);
            foreach (var section in Sections)
            foreach (var row in section.Rows)
                RowsByKey[row.Key] = row;
            // Update can synchronously prune rows or complete a request. Capture its final revision.
            _rowsKey = (
                hub.Catalog,
                hub.Catalog.Revision,
                hub.ProjectsRevision,
                Worktrees.Revision,
                _projectFilterRevision,
                _query,
                _kind
            );
            _rowsReady = true;
        }
    }
}
