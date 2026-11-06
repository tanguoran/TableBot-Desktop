using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WhaleDesk
{
    [CreateAssetMenu(menuName = "Whale Desk/File Asset Catalog")]
    public sealed class DesktopFileCatalog : ScriptableObject
    {
        public DesktopFileAsset[] assets = Array.Empty<DesktopFileAsset>();
        public DesktopFileAsset Find(string kind) => assets.FirstOrDefault(a => a.kind == kind) ?? assets.FirstOrDefault(a => a.kind == "other");

        public static string Kind(string path, bool directory)
        {
            if (directory) return "folder";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".doc", ".docx", ".odt", ".pdf", ".epub", ".rtf" }.Contains(ext)) return "document";
            if (new[] { ".xls", ".xlsx", ".ods", ".csv", ".tsv" }.Contains(ext)) return "spreadsheet";
            if (new[] { ".ppt", ".pptx", ".odp" }.Contains(ext)) return "slides";
            if (new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".svg", ".psd" }.Contains(ext)) return "image";
            if (new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" }.Contains(ext)) return "audio";
            if (new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv" }.Contains(ext)) return "video";
            if (new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" }.Contains(ext)) return "archive";
            if (new[] { ".txt", ".md", ".json", ".yaml", ".yml", ".xml", ".html", ".css", ".js", ".ts", ".py", ".cs", ".cpp", ".h", ".bat", ".cmd", ".ps1", ".sh", ".log", ".ini" }.Contains(ext)) return "text";
            if (new[] { ".exe", ".msi", ".msix", ".appx" }.Contains(ext)) return "program";
            return "other";
        }

        public static int Order(string kind) => Array.IndexOf(new[] { "folder", "document", "spreadsheet", "slides", "image", "audio", "video", "archive", "text", "program", "other" }, kind);
    }

    [Serializable] public sealed class DesktopFileAsset
    {
        public string kind;
        public GameObject prefab;
        public float yaw = 180;
        public float maximumWidth = .18f, maximumDepth = .16f, maximumHeight = .20f;
        public Color accent = Color.white;
        public bool blocksNavigation = true;
    }

    public sealed class DesktopFileNameComparer : IComparer<string>
    {
        public static readonly DesktopFileNameComparer Instance = new DesktopFileNameComparer();
        public int Compare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int ai = i, bj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string an = a.Substring(ai, i - ai).TrimStart('0'), bn = b.Substring(bj, j - bj).TrimStart('0');
                    int numeric = an.Length.CompareTo(bn.Length);
                    if (numeric == 0) numeric = string.CompareOrdinal(an, bn);
                    if (numeric != 0) return numeric;
                }
                else
                {
                    int result = char.ToUpperInvariant(a[i++]).CompareTo(char.ToUpperInvariant(b[j++]));
                    if (result != 0) return result;
                }
            }
            int remaining = (a.Length - i).CompareTo(b.Length - j);
            return remaining != 0 ? remaining : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }
    }
}
