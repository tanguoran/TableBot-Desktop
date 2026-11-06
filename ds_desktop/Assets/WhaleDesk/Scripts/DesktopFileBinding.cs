using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace WhaleDesk
{
    [DisallowMultipleComponent]
    public sealed class DesktopFileBinding : MonoBehaviour
    {
        public DesktopFileSync owner;
        public Transform physicalRoot;
        public string FileId { get; private set; }
        public DesktopFileEntry Entry { get; private set; }
        public bool OpenedLastTime { get; private set; }
#if UNITY_EDITOR
        public Action<string> ExternalOpenForTests;
#endif

        public void Bind(DesktopFileEntry entry)
        {
            Entry = entry; FileId = entry.id;
            name = "Desktop File - " + entry.name;
            var perceived = GetComponent<DesktopWorldObject>();
            perceived.objectId = entry.id; perceived.displayName = entry.name;
            perceived.objectType = "file";
            perceived.tags = new[] { "desktop_file", entry.kind, entry.extension.TrimStart('.') };
            perceived.physicalRoot = physicalRoot;
            if (!GetComponent<DesktopFileInteraction>()) gameObject.AddComponent<DesktopFileInteraction>();
        }

        public bool OpenExternal()
        {
            OpenedLastTime = false;
            if (!owner || !owner.TryResolve(FileId, out var entry) ||
                !string.Equals(entry.path, Entry.path, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (!(entry.directory ? Directory.Exists(entry.path) : File.Exists(entry.path)))
                { owner.RequestSync(); owner.LastInteractionError = "文件已不存在，正在同步"; return false; }
                var info = entry.directory ? (FileSystemInfo)new DirectoryInfo(entry.path) : new FileInfo(entry.path);
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.path)), Path.GetFullPath(entry.root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                    !DesktopFileScanner.IsIncluded(entry.path, info.Attributes) || DesktopNativeFileId.Read(entry.path, entry.directory, info.CreationTimeUtc.Ticks) != entry.identity)
                { owner.RequestSync(); owner.LastInteractionError = "文件已变化，正在同步"; return false; }
#if UNITY_EDITOR
                if (ExternalOpenForTests != null) { ExternalOpenForTests(entry.path); OpenedLastTime = true; return true; }
#endif
                Process.Start(new ProcessStartInfo { FileName = entry.path, UseShellExecute = true });
                OpenedLastTime = true; owner.LastInteractionError = ""; return true;
            }
            catch (Exception error) { owner.LastInteractionError = "无法打开：" + error.Message; return false; }
        }

        public DesktopFileMetadata Capture() => new DesktopFileMetadata {
            file_id = FileId, name = Entry.name, extension = Entry.extension, kind = Entry.kind,
            is_directory = Entry.directory, size_bytes = Entry.size,
            modified_utc = new DateTime(Entry.modified_ticks, DateTimeKind.Utc).ToString("O"),
            visible = gameObject.activeInHierarchy, available_locally = Entry.AvailableLocally
        };
    }

    [Serializable] public sealed class DesktopFileMetadata
    {
        public string file_id, name, extension, kind, modified_utc;
        public bool is_directory, visible, available_locally;
        public long size_bytes;
    }
}
