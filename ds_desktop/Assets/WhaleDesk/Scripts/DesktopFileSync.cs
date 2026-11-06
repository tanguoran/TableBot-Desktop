using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WhaleDesk
{
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(DesktopFileLayout))]
    public sealed class DesktopFileSync : MonoBehaviour
    {
        public DesktopFileCatalog catalog;
        public DesktopNavigation navigation;
        public DesktopWalker walker;
        public Camera viewCamera;
        public GameObject[] placeholderProps = Array.Empty<GameObject>();
        [Tooltip("Empty uses the Windows user and public Desktop folders. For isolated tests only.")]
        public string[] sourceRootsOverride = Array.Empty<string>();
        public string storageDirectoryOverride = "";
        public float reconciliationSeconds = 5, debounceSeconds = .4f;
        public string LastInteractionError { get; set; } = "";
        public string LastSyncError { get; private set; } = "";
        public int Revision { get; private set; }
        public int Page { get; private set; }
        public bool IsScanning => scan != null;
        public DesktopFileLayout Layout { get; private set; }
        public IReadOnlyDictionary<string, DesktopFileEntry> Entries => entries;
        public IEnumerable<DesktopFileBinding> Visible => bindings.Values.Where(b => b && b.gameObject.activeInHierarchy);
        public int PageCount => Mathf.Max(1, entries.Count == 0 ? 1 : entries.Values.Max(e => e.slot) / (Layout ? Layout.Capacity : 1) + 1);
        readonly Dictionary<string, DesktopFileEntry> entries = new Dictionary<string, DesktopFileEntry>();
        readonly Dictionary<string, DesktopFileBinding> bindings = new Dictionary<string, DesktopFileBinding>();
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly ConcurrentQueue<(string oldPath, string newPath)> renames = new ConcurrentQueue<(string, string)>();
        readonly Dictionary<string, string> pendingRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Task<DesktopFileScan> scan;
        string[] roots;
        string manifestPath;
        volatile bool disposed;
        int dirty;
        long changedAt;
        float nextReconcile, nextVisualCheck;
        bool visualDirty, tidyRequested;
        int requestedPage = -1;
        string selectedId, lastClickId;
        float lastClickTime;
        readonly Queue<string> thumbnailQueue = new Queue<string>();
        Task<byte[]> thumbnailRead;
        string thumbnailId;
        Font uiFont;
        bool ownsFont;
        GUIStyle labelStyle, selectedStyle, detailStyle, buttonStyle;
        Texture2D labelBackground;

        void OnEnable()
        {
            disposed = false;
            if (roots != null) { SetRoots(); RequestSync(true); visualDirty = true; }
        }

        void Start()
        {
            Layout = GetComponent<DesktopFileLayout>(); Layout.navigation = navigation;
            foreach (var prop in placeholderProps) if (prop) prop.SetActive(false);
            Layout.BuildSlots();
            string storage = string.IsNullOrWhiteSpace(storageDirectoryOverride) ?
                Path.Combine(Application.persistentDataPath, "DesktopFiles") : Path.GetFullPath(storageDirectoryOverride);
            manifestPath = Path.Combine(storage, "manifest.json");
            try
            {
                if (File.Exists(manifestPath))
                    foreach (var entry in JsonUtility.FromJson<DesktopFileManifest>(File.ReadAllText(manifestPath)).entries ?? Array.Empty<DesktopFileEntry>())
                        if (!string.IsNullOrEmpty(entry.id) && !string.IsNullOrEmpty(entry.path)) entries[entry.id] = entry;
            }
            catch (Exception error) { LastSyncError = "布局缓存未能读取：" + error.Message; }
            SetRoots();
            nextReconcile = Time.realtimeSinceStartup + reconciliationSeconds;
            RequestSync(true);
        }

        void SetRoots()
        {
            string[] next = sourceRootsOverride.Length > 0 ? sourceRootsOverride.Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : DesktopFileScanner.DesktopRoots();
            if (roots != null && roots.SequenceEqual(next, StringComparer.OrdinalIgnoreCase) && watchers.Count == roots.Count(Directory.Exists)) return;
            foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); roots = next;
            foreach (var root in roots.Where(Directory.Exists))
            {
                try
                {
                    var watcher = new FileSystemWatcher(root) {
                        IncludeSubdirectories = false, InternalBufferSize = 32768,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
                    };
                    watcher.Created += (_, e) => MarkDirty(); watcher.Deleted += (_, e) => MarkDirty();
                    watcher.Changed += (_, e) => MarkDirty();
                    watcher.Renamed += (_, e) => { renames.Enqueue((e.OldFullPath, e.FullPath)); MarkDirty(); };
                    watcher.Error += (_, e) => MarkDirty();
                    watcher.EnableRaisingEvents = true; watchers.Add(watcher);
                }
                catch (Exception error) { LastSyncError = "实时监听暂不可用，使用定时核对：" + error.Message; }
            }
        }

        void MarkDirty()
        {
            if (disposed) return;
            Interlocked.Exchange(ref changedAt, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref dirty, 1);
        }

        public void RequestSync(bool immediate = false)
        {
            MarkDirty();
            if (immediate) Interlocked.Exchange(ref changedAt, 0);
        }

        void OnApplicationFocus(bool focus) { if (focus && roots != null) { SetRoots(); RequestSync(true); } }

        void Update()
        {
            if (roots == null) return;
            if (scan != null && scan.IsCompleted)
            {
                var finished = scan; scan = null;
                try { Apply(finished.GetAwaiter().GetResult()); }
                catch (Exception error) { LastSyncError = "同步失败，稍后重试：" + error.Message; }
            }
            if (Time.realtimeSinceStartup >= nextReconcile)
            {
                SetRoots(); RequestSync(true); nextReconcile = Time.realtimeSinceStartup + Mathf.Max(1, reconciliationSeconds);
            }
            if (scan == null && Volatile.Read(ref dirty) != 0 &&
                (DateTime.UtcNow.Ticks - Interlocked.Read(ref changedAt)) / (double)TimeSpan.TicksPerSecond >= debounceSeconds)
            {
                Interlocked.Exchange(ref dirty, 0);
                while (renames.TryDequeue(out var renamed)) pendingRenames[renamed.newPath] = renamed.oldPath;
                var sources = roots.ToArray(); scan = Task.Run(() => DesktopFileScanner.Scan(sources));
            }
            bool busy = walker && (walker.IsMoving || walker.GetComponent<DesktopActionRunner>().IsBusy);
            if (!busy && tidyRequested)
            {
                tidyRequested = false; DesktopFileLayout.AssignSlots(entries.Values, true); SaveManifest();
                Revision++; visualDirty = true;
            }
            if (!busy && requestedPage >= 0)
            {
                Page = Mathf.Clamp(requestedPage, 0, PageCount - 1); requestedPage = -1; visualDirty = true;
            }
            if (visualDirty && Time.realtimeSinceStartup >= nextVisualCheck)
            { nextVisualCheck = Time.realtimeSinceStartup + .2f; ReconcileVisuals(); }
            UpdateThumbnails();
        }

        void Apply(DesktopFileScan result)
        {
            var previous = entries.Values.ToArray();
            var byIdentity = previous.Where(e => !string.IsNullOrEmpty(e.identity)).GroupBy(e => e.identity).ToDictionary(g => g.Key, g => g.ToArray());
            var byPath = previous.GroupBy(e => e.path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var scannedPaths = new HashSet<string>(result.entries.Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            var next = new Dictionary<string, DesktopFileEntry>();
            foreach (var item in result.entries)
            {
                DesktopFileEntry old = null;
                if (byPath.TryGetValue(item.path, out var atPath) && atPath.identity == item.identity) old = atPath;
                if (old == null && pendingRenames.TryGetValue(item.path, out var oldPath) && byPath.TryGetValue(oldPath, out var renamed))
                {
                    bool fallback = renamed.identity.StartsWith("path:") && item.identity.StartsWith("path:") &&
                        renamed.identity.Substring(renamed.identity.LastIndexOf(':')) == item.identity.Substring(item.identity.LastIndexOf(':'));
                    if (renamed.identity == item.identity || fallback) old = renamed;
                }
                if (old == null && byIdentity.TryGetValue(item.identity, out var candidates))
                    old = candidates.FirstOrDefault(e => !next.ContainsKey(e.id) && !scannedPaths.Contains(e.path));
                item.id = old?.id ?? "file_" + Guid.NewGuid().ToString("N"); item.slot = old?.slot ?? -1;
                next[item.id] = item;
            }
            // An unreadable directory/file is not evidence that its contents were deleted.
            foreach (var old in previous)
                if (result.failedRoots.Contains(old.root) || result.failedPaths.Contains(old.path)) next.TryAdd(old.id, old);
            pendingRenames.Clear();
            bool changed = next.Count != entries.Count || next.Values.Any(e => !entries.TryGetValue(e.id, out var old) ||
                e.path != old.path || e.name != old.name || e.kind != old.kind || e.extension != old.extension || e.modified_ticks != old.modified_ticks || e.size != old.size || e.attributes != old.attributes);
            DesktopFileLayout.AssignSlots(next.Values, false);
            // Rebuild image previews only for actual writes, not for periodic reconciliation.
            foreach (var entry in next.Values)
                if (entry.kind == "image" && (!entries.TryGetValue(entry.id, out var old) || old.modified_ticks != entry.modified_ticks || old.path != entry.path || old.attributes != entry.attributes))
                    thumbnailQueue.Enqueue(entry.id);
            entries.Clear(); foreach (var entry in next) entries.Add(entry.Key, entry.Value);
            LastSyncError = result.errors.Count == 0 ? "" : string.Join("; ", result.errors.Take(2));
            if (changed) { Revision++; SaveManifest(); visualDirty = true; }
            Page = Mathf.Clamp(Page, 0, PageCount - 1);
            if (entries.Count > 0 && !entries.Values.Any(e => e.slot / Layout.Capacity == Page))
                Page = entries.Values.Min(e => e.slot) / Layout.Capacity;
            if (bindings.Count == 0 && entries.Count > 0) visualDirty = true;
            if (selectedId != null && !entries.ContainsKey(selectedId)) selectedId = null;
        }

        void SaveManifest()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
                string temporary = manifestPath + ".new";
                File.WriteAllText(temporary, JsonUtility.ToJson(new DesktopFileManifest { entries = entries.Values.ToArray() }, true));
                if (File.Exists(manifestPath)) File.Replace(temporary, manifestPath, null); else File.Move(temporary, manifestPath);
            }
            catch (Exception error) { LastSyncError = "布局保存失败：" + error.Message; }
        }

        void ReconcileVisuals()
        {
            visualDirty = false;
            var visible = entries.Values.Where(e => e.slot / Layout.Capacity == Page).ToDictionary(e => e.id);
            foreach (var id in bindings.Keys.ToArray())
            {
                var binding = bindings[id];
                if (!visible.TryGetValue(id, out var entry) || binding.Entry.kind != entry.kind)
                { binding.gameObject.SetActive(false); Destroy(binding.gameObject); bindings.Remove(id); }
            }
            foreach (var entry in visible.Values)
            {
                var position = Layout.Positions[entry.slot % Layout.Capacity];
                if (bindings.TryGetValue(entry.id, out var binding))
                {
                    if (Vector3.Distance(binding.transform.position, position) > .001f && Occupied(position)) { visualDirty = true; continue; }
                    binding.transform.position = position; binding.Bind(entry); continue;
                }
                if (Occupied(position)) { visualDirty = true; continue; }
                bindings.Add(entry.id, DesktopFileVisual.Create(this, entry, position));
                if (entry.kind == "image") thumbnailQueue.Enqueue(entry.id);
            }
        }

        bool Occupied(Vector3 point) => walker && Mathf.Abs(walker.transform.position.x - point.x) < navigation.agentRadius + .13f &&
            Mathf.Abs(walker.transform.position.z - point.z) < navigation.agentRadius + .12f;

        public bool TryResolve(string id, out DesktopFileEntry entry) => entries.TryGetValue(id, out entry);
        public void ShowPage(int page) { requestedPage = Mathf.Clamp(page, 0, PageCount - 1); }
        public void Tidy() { tidyRequested = true; }

        public bool TryHandlePointer(Vector2 screenPoint)
        {
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            if (ToolbarRect().Contains(guiPoint) || selectedId != null && DetailRect().Contains(guiPoint)) return true;
            foreach (var binding in Visible)
                if (LabelRect(binding).Contains(guiPoint)) { ClickFile(binding); return true; }
            if (Physics.Raycast(viewCamera.ScreenPointToRay(screenPoint), out var hit, 100))
            {
                var binding = hit.collider.GetComponentInParent<DesktopFileBinding>();
                if (binding && binding.owner == this) { ClickFile(binding); return true; }
            }
            selectedId = null; return false;
        }

        void ClickFile(DesktopFileBinding binding)
        {
            if (selectedId != binding.FileId) LastInteractionError = "";
            selectedId = binding.FileId;
            if (lastClickId == selectedId && Time.unscaledTime - lastClickTime < .35f) { binding.OpenExternal(); lastClickId = null; }
            else { lastClickId = selectedId; lastClickTime = Time.unscaledTime; }
        }

        Rect ToolbarRect() => new Rect(12, 12, Mathf.Min(440, Screen.width - 24), 39);
        Rect DetailRect() => DesktopChatUI.AboveChat(new Rect(12, Screen.height - 90, Mathf.Min(720, Screen.width - 24), 78));
        Rect LabelRect(DesktopFileBinding binding)
        {
            var point = viewCamera.WorldToScreenPoint(binding.transform.position + new Vector3(0, .015f, -.11f));
            return point.z > 0 ? DesktopChatUI.AboveChat(new Rect(point.x - 73, Screen.height - point.y, 146, 22)) : new Rect(-10000, -10000, 0, 0);
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            var available = Font.GetOSInstalledFontNames();
            string fontName = new[] { "Microsoft YaHei", "Microsoft YaHei UI", "Yu Gothic", "Arial" }.FirstOrDefault(available.Contains);
            ownsFont = fontName != null;
            uiFont = fontName == null ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") : Font.CreateDynamicFontFromOSFont(fontName, 16);
            labelBackground = new Texture2D(1, 1); labelBackground.SetPixel(0, 0, new Color(.035f, .045f, .065f, .83f)); labelBackground.Apply();
            labelStyle = new GUIStyle(GUI.skin.box) { font = uiFont, fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(4, 4, 2, 2) };
            labelStyle.normal.background = labelBackground; labelStyle.normal.textColor = new Color(.92f, .95f, 1);
            selectedStyle = new GUIStyle(labelStyle); selectedStyle.normal.textColor = new Color(.4f, .9f, 1);
            detailStyle = new GUIStyle(labelStyle) { fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont, fontSize = 13 };
        }

        void OnGUI()
        {
            if (Layout == null || !viewCamera) return;
            EnsureStyles();
            foreach (var binding in Visible)
            {
                string label = binding.Entry.name;
                if (label.Length > 16) label = label.Substring(0, 13) + "…";
                label = (binding.Entry.directory ? "[文件夹] " : "[" + (binding.Entry.extension.Length > 1 ? binding.Entry.extension.Substring(1).ToUpperInvariant() : "FILE") + "] ") + label;
                GUI.Box(LabelRect(binding), label, binding.FileId == selectedId ? selectedStyle : labelStyle);
            }
            var bar = ToolbarRect(); GUI.Box(bar, "", labelStyle);
            GUI.Label(new Rect(bar.x + 9, bar.y + 9, 190, 24), $"桌面文件 {entries.Count} 项 · {Page + 1}/{PageCount}", detailStyle);
            if (GUI.Button(new Rect(bar.x + 195, bar.y + 6, 35, 27), "‹", buttonStyle)) ShowPage(Page - 1);
            if (GUI.Button(new Rect(bar.x + 234, bar.y + 6, 35, 27), "›", buttonStyle)) ShowPage(Page + 1);
            if (GUI.Button(new Rect(bar.x + 275, bar.y + 6, 65, 27), "同步", buttonStyle)) { SetRoots(); RequestSync(true); }
            if (GUI.Button(new Rect(bar.x + 345, bar.y + 6, 65, 27), "整理", buttonStyle)) Tidy();
            if (selectedId != null && entries.TryGetValue(selectedId, out var selected))
            {
                string size = selected.directory ? "文件夹" : selected.size >= 1048576 ? $"{selected.size / 1048576d:F1} MB" : $"{selected.size / 1024d:F1} KB";
                GUI.Box(DetailRect(), selected.name + "\n" + size + "  ·  双击用默认程序打开" +
                    (string.IsNullOrEmpty(LastInteractionError) ? "" : "\n" + LastInteractionError), detailStyle);
            }
            if (!string.IsNullOrEmpty(LastSyncError)) GUI.Box(new Rect(12, 57, Mathf.Min(720, Screen.width - 24), 40), "同步暂未完成：" + LastSyncError, detailStyle);
        }

        void UpdateThumbnails()
        {
            if (thumbnailRead != null && thumbnailRead.IsCompleted)
            {
                var task = thumbnailRead; thumbnailRead = null;
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null && bindings.TryGetValue(thumbnailId, out var binding))
                    binding.GetComponent<DesktopFileVisual>().SetImage(task.Result);
            }
            if (thumbnailRead != null) return;
            while (thumbnailQueue.Count > 0)
            {
                string id = thumbnailQueue.Dequeue();
                if (!bindings.ContainsKey(id) || !entries.TryGetValue(id, out var entry) || entry.kind != "image" || !entry.AvailableLocally) continue;
                if (entry.extension != ".png" && entry.extension != ".jpg" && entry.extension != ".jpeg") continue;
                thumbnailId = id; string path = entry.path;
                thumbnailRead = Task.Run(() => {
                    try { var info = new FileInfo(path); return info.Length <= 16 * 1024 * 1024 ? File.ReadAllBytes(path) : null; }
                    catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
                });
                break;
            }
        }

        public DesktopFilesSnapshot Capture() => new DesktopFilesSnapshot {
            revision = Revision, total = entries.Count, page = Page + 1, page_count = PageCount,
            syncing = IsScanning, error = LastSyncError,
            entries = DesktopFileLayout.Ordered(entries.Values).Select(e => new DesktopFileMetadata {
                file_id = e.id, name = e.name, extension = e.extension, kind = e.kind, is_directory = e.directory,
                size_bytes = e.size, modified_utc = new DateTime(e.modified_ticks, DateTimeKind.Utc).ToString("O"), available_locally = e.AvailableLocally,
                visible = bindings.TryGetValue(e.id, out var binding) && binding && binding.gameObject.activeInHierarchy
            }).ToArray()
        };

        void OnDisable()
        {
            disposed = true; foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
            foreach (var binding in bindings.Values) if (binding) { binding.gameObject.SetActive(false); Destroy(binding.gameObject); }
            bindings.Clear();
            if (labelBackground) Destroy(labelBackground);
            if (uiFont && ownsFont) Destroy(uiFont);
            labelStyle = selectedStyle = detailStyle = buttonStyle = null; labelBackground = null; uiFont = null;
        }
    }

    [Serializable] public sealed class DesktopFilesSnapshot
    {
        public int revision, total, page, page_count;
        public bool syncing;
        public string error;
        public DesktopFileMetadata[] entries;
    }
}
