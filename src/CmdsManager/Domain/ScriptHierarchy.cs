using System;
using System.Collections.Generic;
using System.Linq;

namespace CmdsManager.Domain
{
    public enum HierarchyItemKind
    {
        Folder,
        Script
    }

    public sealed class HierarchyItemKey : IEquatable<HierarchyItemKey>
    {
        public HierarchyItemKey(HierarchyItemKind kind, Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("A hierarchy item requires a non-empty identifier.", nameof(id));
            Kind = kind;
            Id = id;
        }

        public HierarchyItemKind Kind { get; }
        public Guid Id { get; }

        public static HierarchyItemKey Folder(Guid id) { return new HierarchyItemKey(HierarchyItemKind.Folder, id); }
        public static HierarchyItemKey Script(Guid id) { return new HierarchyItemKey(HierarchyItemKind.Script, id); }

        public bool Equals(HierarchyItemKey other)
        {
            return other != null && Kind == other.Kind && Id == other.Id;
        }

        public override bool Equals(object obj) { return Equals(obj as HierarchyItemKey); }
        public override int GetHashCode() { return ((int)Kind * 397) ^ Id.GetHashCode(); }
    }

    public static class ScriptHierarchy
    {
        public static IReadOnlyList<HierarchyItemKey> GetChildren(AppConfiguration configuration, Guid? parentFolderId)
        {
            EnsureConfiguration(configuration);
            var children = new List<Tuple<HierarchyItemKey, int, string>>();
            children.AddRange(configuration.Folders
                .Where(folder => Nullable.Equals(folder.ParentFolderId, parentFolderId))
                .Select(folder => Tuple.Create(HierarchyItemKey.Folder(folder.Id), folder.SortOrder, folder.Name ?? string.Empty)));
            children.AddRange(configuration.Scripts
                .Where(script => Nullable.Equals(script.FolderId, parentFolderId))
                .Select(script => Tuple.Create(HierarchyItemKey.Script(script.Id), script.SortOrder, script.Name ?? string.Empty)));
            return children.OrderBy(item => item.Item2)
                .ThenBy(item => item.Item3, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Item1.Kind)
                .ThenBy(item => item.Item1.Id)
                .Select(item => item.Item1)
                .ToArray();
        }

        public static IReadOnlyList<ScriptDefinition> GetDescendantScripts(AppConfiguration configuration, Guid folderId)
        {
            EnsureConfiguration(configuration);
            if (!configuration.Folders.Any(folder => folder.Id == folderId)) return new ScriptDefinition[0];
            var scripts = new List<ScriptDefinition>();
            AppendDescendantScripts(configuration, folderId, scripts, new HashSet<Guid>());
            return scripts;
        }

        public static IReadOnlyList<ScriptDefinition> GetAllScriptsInDisplayOrder(AppConfiguration configuration)
        {
            EnsureConfiguration(configuration);
            var scripts = new List<ScriptDefinition>();
            AppendChildrenScripts(configuration, null, scripts, new HashSet<Guid>());
            return scripts;
        }

        public static IReadOnlyList<Guid> GetAncestorFolderIds(AppConfiguration configuration, Guid? folderId)
        {
            EnsureConfiguration(configuration);
            var ancestors = new List<Guid>();
            var visited = new HashSet<Guid>();
            while (folderId.HasValue)
            {
                if (!visited.Add(folderId.Value)) throw new InvalidOperationException("Folder hierarchy contains a cycle.");
                var folder = configuration.Folders.FirstOrDefault(item => item.Id == folderId.Value);
                if (folder == null) break;
                ancestors.Add(folder.Id);
                folderId = folder.ParentFolderId;
            }
            ancestors.Reverse();
            return ancestors;
        }

        public static bool IsDescendantFolder(AppConfiguration configuration, Guid folderId, Guid possibleAncestorId)
        {
            EnsureConfiguration(configuration);
            var current = configuration.Folders.FirstOrDefault(item => item.Id == folderId);
            var visited = new HashSet<Guid>();
            while (current != null && current.ParentFolderId.HasValue)
            {
                if (!visited.Add(current.Id)) return false;
                if (current.ParentFolderId.Value == possibleAncestorId) return true;
                current = configuration.Folders.FirstOrDefault(item => item.Id == current.ParentFolderId.Value);
            }
            return false;
        }

        public static int GetNextSortOrder(AppConfiguration configuration, Guid? parentFolderId)
        {
            var children = GetChildren(configuration, parentFolderId);
            if (children.Count == 0) return 0;
            return children.Select(item => GetSortOrder(configuration, item)).Max() + 10;
        }

        public static void AppendFolder(AppConfiguration configuration, ScriptFolderDefinition folder, Guid? parentFolderId)
        {
            EnsureConfiguration(configuration);
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            folder.ParentFolderId = parentFolderId;
            folder.SortOrder = GetNextSortOrder(configuration, parentFolderId);
            configuration.Folders.Add(folder);
            NormalizeChildren(configuration, parentFolderId);
        }

        public static void AppendScript(AppConfiguration configuration, ScriptDefinition script, Guid? parentFolderId)
        {
            EnsureConfiguration(configuration);
            if (script == null) throw new ArgumentNullException(nameof(script));
            script.FolderId = parentFolderId;
            script.SortOrder = GetNextSortOrder(configuration, parentFolderId);
            configuration.Scripts.Add(script);
            NormalizeChildren(configuration, parentFolderId);
        }

        public static void MoveItem(AppConfiguration configuration, HierarchyItemKey item, Guid? targetParentFolderId,
            int targetIndex)
        {
            EnsureConfiguration(configuration);
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (targetParentFolderId.HasValue && !configuration.Folders.Any(folder => folder.Id == targetParentFolderId.Value))
                throw new InvalidOperationException("The target folder does not exist.");

            var previousParent = GetParentFolderId(configuration, item);
            if (item.Kind == HierarchyItemKind.Folder)
            {
                if (item.Id == targetParentFolderId ||
                    (targetParentFolderId.HasValue && IsDescendantFolder(configuration, targetParentFolderId.Value, item.Id)))
                    throw new InvalidOperationException("A folder cannot be moved into itself or one of its descendants.");
            }

            var targetChildren = GetChildren(configuration, targetParentFolderId)
                .Where(child => !child.Equals(item)).ToList();
            targetIndex = Math.Max(0, Math.Min(targetIndex, targetChildren.Count));
            targetChildren.Insert(targetIndex, item);
            SetParentFolderId(configuration, item, targetParentFolderId);
            ApplyOrder(configuration, targetChildren);
            if (!Nullable.Equals(previousParent, targetParentFolderId)) NormalizeChildren(configuration, previousParent);
        }

        public static void RemoveFolderKeepingContents(AppConfiguration configuration, Guid folderId)
        {
            EnsureConfiguration(configuration);
            var folder = configuration.Folders.FirstOrDefault(item => item.Id == folderId);
            if (folder == null) return;
            var parent = folder.ParentFolderId;
            var parentChildren = GetChildren(configuration, parent).ToList();
            var folderIndex = parentChildren.FindIndex(item => item.Kind == HierarchyItemKind.Folder && item.Id == folderId);
            if (folderIndex < 0) folderIndex = parentChildren.Count;
            parentChildren.RemoveAll(item => item.Kind == HierarchyItemKind.Folder && item.Id == folderId);

            var promoted = GetChildren(configuration, folderId).ToList();
            configuration.Folders.RemoveAll(item => item.Id == folderId);
            foreach (var child in promoted) SetParentFolderId(configuration, child, parent);
            parentChildren.InsertRange(Math.Min(folderIndex, parentChildren.Count), promoted);
            ApplyOrder(configuration, parentChildren);
        }

        public static void NormalizeAll(AppConfiguration configuration)
        {
            EnsureConfiguration(configuration);
            NormalizeBranch(configuration, null, new HashSet<Guid>());
        }

        public static void Validate(AppConfiguration configuration)
        {
            EnsureConfiguration(configuration);
            var allIds = new HashSet<Guid>();
            var folderIds = new HashSet<Guid>();
            foreach (var folder in configuration.Folders)
            {
                if (folder == null || folder.Id == Guid.Empty) throw new InvalidOperationException("Every folder requires a non-empty identifier.");
                if (!allIds.Add(folder.Id)) throw new InvalidOperationException("Folder and script identifiers must be unique.");
                folderIds.Add(folder.Id);
                if (string.IsNullOrWhiteSpace(folder.Name)) throw new InvalidOperationException("Every folder requires a name.");
                if (!Enum.IsDefined(typeof(FolderIconKind), folder.Icon)) throw new InvalidOperationException("A folder uses an unsupported icon.");
                if (folder.ParentFolderId == folder.Id) throw new InvalidOperationException("A folder cannot be its own parent.");
            }

            foreach (var folder in configuration.Folders)
            {
                if (folder.ParentFolderId.HasValue && !folderIds.Contains(folder.ParentFolderId.Value))
                    throw new InvalidOperationException("A folder references a missing parent folder.");
                var visited = new HashSet<Guid>();
                var current = folder;
                while (current != null)
                {
                    if (!visited.Add(current.Id)) throw new InvalidOperationException("Folder hierarchy contains a cycle.");
                    current = current.ParentFolderId.HasValue
                        ? configuration.Folders.FirstOrDefault(item => item.Id == current.ParentFolderId.Value)
                        : null;
                }
            }

            foreach (var script in configuration.Scripts)
            {
                if (script == null || script.Id == Guid.Empty) throw new InvalidOperationException("Every script requires a non-empty identifier.");
                if (!allIds.Add(script.Id)) throw new InvalidOperationException("Folder and script identifiers must be unique.");
                if (script.FolderId.HasValue && !folderIds.Contains(script.FolderId.Value))
                    throw new InvalidOperationException("A script references a missing folder.");
            }
        }

        private static void AppendDescendantScripts(AppConfiguration configuration, Guid folderId,
            ICollection<ScriptDefinition> result, ISet<Guid> visited)
        {
            if (!visited.Add(folderId)) return;
            AppendChildrenScripts(configuration, folderId, result, visited);
        }

        private static void AppendChildrenScripts(AppConfiguration configuration, Guid? parentFolderId,
            ICollection<ScriptDefinition> result, ISet<Guid> visited)
        {
            foreach (var child in GetChildren(configuration, parentFolderId))
            {
                if (child.Kind == HierarchyItemKind.Script)
                {
                    var script = configuration.Scripts.FirstOrDefault(item => item.Id == child.Id);
                    if (script != null) result.Add(script);
                }
                else if (visited.Add(child.Id))
                {
                    AppendChildrenScripts(configuration, child.Id, result, visited);
                }
            }
        }

        private static void NormalizeBranch(AppConfiguration configuration, Guid? parentFolderId, ISet<Guid> visited)
        {
            NormalizeChildren(configuration, parentFolderId);
            foreach (var folder in configuration.Folders.Where(item => Nullable.Equals(item.ParentFolderId, parentFolderId)).ToArray())
            {
                if (visited.Add(folder.Id)) NormalizeBranch(configuration, folder.Id, visited);
            }
        }

        private static void NormalizeChildren(AppConfiguration configuration, Guid? parentFolderId)
        {
            ApplyOrder(configuration, GetChildren(configuration, parentFolderId));
        }

        private static void ApplyOrder(AppConfiguration configuration, IEnumerable<HierarchyItemKey> items)
        {
            var order = 0;
            foreach (var item in items)
            {
                SetSortOrder(configuration, item, order);
                order += 10;
            }
        }

        private static Guid? GetParentFolderId(AppConfiguration configuration, HierarchyItemKey item)
        {
            if (item.Kind == HierarchyItemKind.Folder)
            {
                var folder = configuration.Folders.FirstOrDefault(value => value.Id == item.Id);
                if (folder == null) throw new InvalidOperationException("The folder does not exist.");
                return folder.ParentFolderId;
            }
            var script = configuration.Scripts.FirstOrDefault(value => value.Id == item.Id);
            if (script == null) throw new InvalidOperationException("The script does not exist.");
            return script.FolderId;
        }

        private static void SetParentFolderId(AppConfiguration configuration, HierarchyItemKey item, Guid? parentFolderId)
        {
            if (item.Kind == HierarchyItemKind.Folder)
            {
                var folder = configuration.Folders.FirstOrDefault(value => value.Id == item.Id);
                if (folder == null) throw new InvalidOperationException("The folder does not exist.");
                folder.ParentFolderId = parentFolderId;
                return;
            }
            var script = configuration.Scripts.FirstOrDefault(value => value.Id == item.Id);
            if (script == null) throw new InvalidOperationException("The script does not exist.");
            script.FolderId = parentFolderId;
        }

        private static int GetSortOrder(AppConfiguration configuration, HierarchyItemKey item)
        {
            return item.Kind == HierarchyItemKind.Folder
                ? configuration.Folders.First(value => value.Id == item.Id).SortOrder
                : configuration.Scripts.First(value => value.Id == item.Id).SortOrder;
        }

        private static void SetSortOrder(AppConfiguration configuration, HierarchyItemKey item, int sortOrder)
        {
            if (item.Kind == HierarchyItemKind.Folder)
                configuration.Folders.First(value => value.Id == item.Id).SortOrder = sortOrder;
            else
                configuration.Scripts.First(value => value.Id == item.Id).SortOrder = sortOrder;
        }

        private static void EnsureConfiguration(AppConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (configuration.Folders == null || configuration.Scripts == null)
                throw new InvalidOperationException("The hierarchy configuration is incomplete.");
        }
    }
}
