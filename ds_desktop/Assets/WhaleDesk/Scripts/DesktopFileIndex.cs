using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace WhaleDesk
{
    // Scanner contains no Unity operations and can run on a background thread.
    public static class DesktopFileScanner
    {
        public static string[] DesktopRoots() => new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public static bool IsIncluded(string path, FileAttributes attributes)
        {
            if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
            if ((attributes & FileAttributes.ReparsePoint) != 0 && DesktopNativeFileId.IsNameSurrogate(path, (attributes & FileAttributes.Directory) != 0)) return false;
            if ((attributes & FileAttributes.Directory) != 0) return true;
            string name = Path.GetFileName(path), ext = Path.GetExtension(path);
            return !new[] { ".lnk", ".url", ".appref-ms", ".website" }.Contains(ext, StringComparer.OrdinalIgnoreCase) &&
                !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("~$", StringComparison.Ordinal);
        }

        public static DesktopFileScan Scan(string[] roots)
        {
            var result = new DesktopFileScan();
            foreach (var root in roots)
            {
                string[] paths;
                try { paths = Directory.GetFileSystemEntries(root); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { result.failedRoots.Add(root); result.errors.Add(Path.GetFileName(root) + ": " + e.Message); continue; }
                foreach (var path in paths)
                {
                    try
                    {
                        var attrs = File.GetAttributes(path);
                        if (!IsIncluded(path, attrs)) continue;
                        bool directory = (attrs & FileAttributes.Directory) != 0;
                        var info = directory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                        result.entries.Add(new DesktopFileEntry {
                            path = Path.GetFullPath(path), root = root, name = info.Name,
                            extension = directory ? "" : Path.GetExtension(path).ToLowerInvariant(),
                            kind = DesktopFileCatalog.Kind(path, directory), directory = directory,
                            attributes = (int)attrs,
                            size = directory ? 0 : ((FileInfo)info).Length,
                            modified_ticks = info.LastWriteTimeUtc.Ticks,
                            identity = DesktopNativeFileId.Read(path, directory, info.CreationTimeUtc.Ticks)
                        });
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    { result.failedPaths.Add(Path.GetFullPath(path)); }
                }
            }
            return result;
        }
    }

    public sealed class DesktopFileScan
    {
        public List<DesktopFileEntry> entries = new List<DesktopFileEntry>();
        public HashSet<string> failedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> failedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> errors = new List<string>();
    }

    [Serializable] public sealed class DesktopFileEntry
    {
        public string id, path, root, name, extension, kind, identity;
        public bool directory;
        public long size, modified_ticks;
        public int slot;
        public int attributes;
        public bool AvailableLocally => (attributes & (0x1000 | 0x400000)) == 0;
    }

    [Serializable] public sealed class DesktopFileManifest { public int version = 1; public DesktopFileEntry[] entries; }

    public static class DesktopNativeFileId
    {
        [StructLayout(LayoutKind.Sequential)] struct FileInfoNative
        {
            public uint attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME creation, access, write;
            public uint volume, sizeHigh, sizeLow, links, indexHigh, indexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
        [StructLayout(LayoutKind.Sequential)] struct AttributeTag { public uint attributes, tag; }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTag info, uint size);

        public static bool IsNameSurrogate(string path, bool directory)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return true;
            try
            {
                using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero))
                    if (!handle.IsInvalid && GetFileInformationByHandleEx(handle, 9, out var tag, 8))
                        return (tag.tag & 0x20000000u) != 0;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { }
            return true;
        }

        public static string Read(string path, bool directory, long creation)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, directory ? 0x02000000u : 0, IntPtr.Zero))
                        if (!handle.IsInvalid && GetFileInformationByHandle(handle, out var info))
                            return $"win:{info.volume:x8}:{info.indexHigh:x8}{info.indexLow:x8}:{creation}";
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { }
            }
            return "path:" + Path.GetFullPath(path).ToUpperInvariant() + ":" + creation;
        }
    }
}
