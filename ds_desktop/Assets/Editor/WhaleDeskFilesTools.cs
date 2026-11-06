using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WhaleDesk;

[InitializeOnLoad]
public static class WhaleDeskFilesTools
{
    const string Root = "Assets/WhaleDesk/Files";
    const string CatalogPath = Root + "/DesktopFileCatalog.asset";
    const string Request = "Temp/WhaleDeskFiles.request", Report = "Temp/WhaleDeskFiles.report.json";
    const string Pending = "WhaleDesk.FilesTest", Batch = "WhaleDesk.FilesBatch", Restore = "WhaleDesk.FilesRestore";
    static FileChecks checks;
    static WhaleDeskFilesTools() { EditorApplication.update += Poll; }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    static DesktopFileCatalog EnsureAssets()
    {
        Folder(Root); Folder(Root + "/Prefabs"); Folder(Root + "/Materials");
        var catalog = AssetDatabase.LoadAssetAtPath<DesktopFileCatalog>(CatalogPath);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<DesktopFileCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        string[] kinds = { "folder", "document", "spreadsheet", "slides", "image", "audio", "video", "archive", "text", "program", "other" };
        if (kinds.All(kind => catalog.assets.Any(a => a.kind == kind && a.prefab))) return catalog;
        string[] sources = { "GarageBoxes/GarageBoxEmptyA", "Books/BookD", "Deskware/NotebookB", "Books/BookG", "PhotoFrames/PhotoFrameB", "", "Books/BookF", "BoxSmallA", "Deskware/NotebookA", "BoxSmallB", "BoxSmallC" };
        Color[] colors = { new Color(.75f,.53f,.20f), new Color(.16f,.35f,.68f), new Color(.18f,.49f,.32f), new Color(.75f,.34f,.13f), Color.white,
            new Color(.58f,.72f,.80f), new Color(.20f,.22f,.27f), new Color(.59f,.39f,.22f), new Color(.16f,.42f,.44f), new Color(.25f,.37f,.53f), new Color(.43f,.46f,.49f) };
        var definitions = new List<DesktopFileAsset>();
        for (int i = 0; i < kinds.Length; i++)
        {
            var existing = catalog.assets.FirstOrDefault(a => a.kind == kinds[i] && a.prefab);
            if (existing != null) { definitions.Add(existing); continue; }
            string target = Root + "/Prefabs/File_" + kinds[i] + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target);
            if (!prefab)
            {
                GameObject instance;
                if (kinds[i] == "audio") instance = MakeDisc();
                else
                {
                    string source = "Assets/HQ_ResidentialHouse/Prefabs/" + sources[i] + ".prefab";
                    var original = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                    if (!original) throw new FileNotFoundException(source);
                    instance = UnityEngine.Object.Instantiate(original);
                }
                instance.name = "File " + kinds[i];
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true)) if (script) UnityEngine.Object.DestroyImmediate(script);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.lightmapIndex = -1;
                prefab = PrefabUtility.SaveAsPrefabAsset(instance, target); UnityEngine.Object.DestroyImmediate(instance);
            }
            definitions.Add(new DesktopFileAsset { kind = kinds[i], prefab = prefab, accent = colors[i], yaw = kinds[i] == "audio" ? 0 : 180,
                maximumWidth = .18f, maximumDepth = .16f, maximumHeight = kinds[i] == "image" ? .20f : .16f });
        }
        definitions.AddRange(catalog.assets.Where(a => !kinds.Contains(a.kind)));
        catalog.assets = definitions.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        return catalog;
    }

    static GameObject MakeDisc()
    {
        const int segments = 64;
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            foreach (float radius in new[] { .009f, .065f })
            {
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
                uv.Add(new Vector2(.5f + Mathf.Cos(angle) * radius / .13f, .5f + Mathf.Sin(angle) * radius / .13f));
            }
            if (i < segments)
            {
                int p = i * 2;
                triangles.AddRange(new[] { p, p + 2, p + 1, p + 1, p + 2, p + 3 });
            }
        }
        string meshPath = Root + "/AudioDisc.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (!mesh) { mesh = new Mesh { name = "Audio Disc" }; AssetDatabase.CreateAsset(mesh, meshPath); }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string materialPath = Root + "/Materials/AudioDisc.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, materialPath); }
        material.SetColor("_BaseColor", new Color(.57f, .65f, .74f)); material.SetFloat("_Metallic", .85f); material.SetFloat("_Smoothness", .8f); material.SetFloat("_Cull", 0);
        var root = new GameObject("Audio Disc"); root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = material;
        return root;
    }

    public static void ConfigureScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before installing desktop files.");
        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        if (!walker) throw new InvalidOperationException("Open WhaleDesk and install navigation first.");
        var sync = UnityEngine.Object.FindFirstObjectByType<DesktopFileSync>();
        if (!sync) sync = new GameObject("08 - Real Desktop Files").AddComponent<DesktopFileSync>();
        sync.catalog = EnsureAssets(); sync.navigation = walker.navigation; sync.walker = walker; sync.viewCamera = Camera.main;
        sync.GetComponent<DesktopFileLayout>().navigation = walker.navigation;
        sync.placeholderProps = new[] { "Reference Books", "Open Task Notebook", "Current Reading", "Reading Stack", "Reminder Notes" }
            .Select(GameObject.Find).Where(g => g).ToArray();
        var clicks = UnityEngine.Object.FindFirstObjectByType<DesktopClickToMove>(); if (clicks) { clicks.files = sync; EditorUtility.SetDirty(clicks); }
        var world = UnityEngine.Object.FindFirstObjectByType<DesktopWorldState>(); if (world) { world.files = sync; EditorUtility.SetDirty(world); }
        EditorUtility.SetDirty(sync); EditorUtility.SetDirty(sync.GetComponent<DesktopFileLayout>());
    }

    [MenuItem("Whale Desk/Install Real Desktop Files")]
    public static void Install()
    {
        WhaleDeskMcpTools.ConfigureScene();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Report, "{\"installed\":true}");
    }

    public static void BatchInstallAndTest()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/WhaleDesk.unity"); Install();
            SessionState.SetBool(Batch, true); BeginTest();
        }
        catch (Exception error) { Fail(error); EditorApplication.Exit(1); }
    }

    static void BeginTest()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
        string fixture = Path.GetFullPath("Temp/WhaleFilesTest-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(fixture, "source"); Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "资料夹")); File.WriteAllText(Path.Combine(source, "资料夹/nested.txt"), "not expanded");
        File.WriteAllText(Path.Combine(source, "报告.pdf"), "fixture PDF");
        File.WriteAllText(Path.Combine(source, "notes2.txt"), "two"); File.WriteAllText(Path.Combine(source, "notes10.txt"), "ten");
        File.WriteAllText(Path.Combine(source, "ignore.lnk"), "fixture shortcut"); File.WriteAllText(Path.Combine(source, "ignore.url"), "fixture shortcut");
        File.WriteAllText(Path.Combine(source, "desktop.ini"), "metadata"); File.WriteAllText(Path.Combine(source, "~$lock.docx"), "temporary");
        File.WriteAllText(Path.Combine(source, "hidden.txt"), "hidden"); File.SetAttributes(Path.Combine(source, "hidden.txt"), FileAttributes.Hidden);
        var image = new Texture2D(16, 16, TextureFormat.RGBA32, false); image.SetPixels(Enumerable.Repeat(Color.cyan, 256).ToArray()); image.Apply();
        File.WriteAllBytes(Path.Combine(source, "图片.png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
        foreach (string name in new[] { "table.xlsx", "slides.pptx", "song.mp3", "movie.mp4", "archive.zip", "app.exe", "unknown.custom" }) File.WriteAllText(Path.Combine(source, name), "fixture");
        var scanned = DesktopFileScanner.Scan(new[] { source });
        if (scanned.entries.Count != 12 || DesktopFileNameComparer.Instance.Compare("notes2.txt", "notes10.txt") >= 0 ||
            !DesktopFileScanner.IsIncluded("real-folder.lnk", FileAttributes.Directory) || new DesktopFileEntry { attributes = 0x1000 }.AvailableLocally)
            throw new Exception("Scanner filtering/top-level/natural-sort check failed: " + scanned.entries.Count);
        SessionState.SetString("WhaleDesk.FilesFixture", fixture);
        var sync = UnityEngine.Object.FindFirstObjectByType<DesktopFileSync>();
        sync.sourceRootsOverride = new[] { source }; sync.storageDirectoryOverride = Path.Combine(fixture, "cache");
        SessionState.SetBool(Pending, true); SessionState.SetBool("WhaleDesk.FilesFailed", false); checks = null; EditorApplication.isPlaying = true;
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Request))
        {
            string command;
            try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); } catch (IOException) { return; }
            try
            {
                if (command == "install") Install();
                else if (command == "test") BeginTest();
                else if (command == "status")
                {
                    var sync = UnityEngine.Object.FindFirstObjectByType<DesktopFileSync>();
                    File.WriteAllText(Report, JsonUtility.ToJson(sync.Capture(), true));
                }
            }
            catch (Exception error) { Fail(error); }
        }
        if (EditorApplication.isPlaying && SessionState.GetBool(Pending, false) && Time.time > 2)
        {
            try
            {
                if (checks == null) checks = new FileChecks();
                if (checks.Tick())
                {
                    SessionState.SetBool(Pending, false); SessionState.SetBool(Restore, true);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                Fail(error); SessionState.SetBool("WhaleDesk.FilesFailed", true); SessionState.SetBool(Pending, false); SessionState.SetBool(Restore, true); EditorApplication.isPlaying = false;
            }
        }
        if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Restore, false))
        {
            SessionState.SetBool(Restore, false);
            var sync = UnityEngine.Object.FindFirstObjectByType<DesktopFileSync>(); sync.sourceRootsOverride = Array.Empty<string>(); sync.storageDirectoryOverride = "";
            EditorSceneManager.MarkSceneDirty(sync.gameObject.scene); EditorSceneManager.SaveScene(sync.gameObject.scene);
            if (SessionState.GetBool(Batch, false)) { SessionState.SetBool(Batch, false); EditorApplication.Exit(SessionState.GetBool("WhaleDesk.FilesFailed", false) ? 1 : 0); }
        }
    }

    static void Fail(Exception error)
    {
        File.WriteAllText(Report, JsonUtility.ToJson(new TestReport { error = error.ToString() }, true)); Debug.LogException(error);
    }

    [Serializable] class TestReport { public bool passed; public string error; public string[] checks; public int files, slots; }

    sealed class FileChecks
    {
        readonly DesktopFileSync sync = UnityEngine.Object.FindFirstObjectByType<DesktopFileSync>();
        readonly List<string> passed = new List<string>();
        readonly string source = Path.Combine(SessionState.GetString("WhaleDesk.FilesFixture", ""), "source");
        int stage; float since; string textId, imageId, folderId, session;
        ActionEvent actionResult;
        DesktopActionRunner runner;
        int manualRevision;

        void Require(bool value, string message) { if (!value) throw new Exception("Desktop files check: " + message); }
        bool WaitFor(Func<bool> ready)
        {
            if (ready()) return true;
            Require(Time.realtimeSinceStartup - since < 12, "stage " + stage + " timed out: " + sync.LastSyncError); return false;
        }
        void Next(string check) { passed.Add(check); stage++; since = Time.realtimeSinceStartup; }

        public bool Tick()
        {
            if (stage == 0)
            {
                if (since == 0) since = Time.realtimeSinceStartup;
                if (!WaitFor(() => sync.Entries.Count == 12 && sync.Visible.Any() && !sync.IsScanning)) return false;
                var snapshot = UnityEngine.Object.FindFirstObjectByType<DesktopWorldState>().Capture(); session = snapshot.scene_session;
                Require(snapshot.desktop_files.total == 12 && snapshot.objects.Count(o => o.object_type == "file") > 0, "MCP file inventory and separate visible objects");
                Require(sync.PageCount > 1 && sync.Visible.Count() <= sync.Layout.Capacity, "bounded pages");
                foreach (var binding in sync.Visible)
                {
                    var box = binding.GetComponent<DesktopWorldObject>().WorldBounds();
                    Require(box.size.x > .02f && box.size.x < .25f && box.size.z < .25f && box.min.y >= sync.navigation.SurfaceHeight - .003f, "normalized prop bounds");
                    Require(binding.GetComponent<DesktopObstacle>() && binding.GetComponent<Collider>(), "file scripts and collider");
                    Require(binding.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m && m.shader.name != "Hidden/InternalErrorShader")), "valid material shaders");
                }
                textId = sync.Entries.Values.First(e => e.name == "notes2.txt").id;
                imageId = sync.Entries.Values.First(e => e.name == "图片.png").id;
                folderId = sync.Entries.Values.First(e => e.directory).id;
                runner = sync.walker.GetComponent<DesktopActionRunner>(); manualRevision = runner.ManualControlRevision;
                Next("Top-level scanner, ignored shortcuts/hidden/temp files, natural sort, prefab scripts, normalized bounds, MCP inventory, pagination");
            }
            if (stage == 1)
            {
                if (!WaitFor(() => sync.Visible.Any(b => b.FileId == imageId && b.GetComponent<DesktopFileVisual>().HasThumbnail))) return false;
                var folder = sync.Visible.First(b => b.FileId == folderId);
                string opened = null; folder.ExternalOpenForTests = path => opened = path;
                Require(folder.OpenExternal() && opened == Path.Combine(source, "资料夹"), "bound external folder opening");
                var point = sync.viewCamera.WorldToScreenPoint(folder.transform.position + Vector3.up * .05f);
                var clicks = UnityEngine.Object.FindFirstObjectByType<DesktopClickToMove>();
                Require(clicks.TryMoveAtScreenPoint(point) && runner.ManualControlRevision == manualRevision && !sync.walker.IsMoving, "file click consumed before walker");
                folder.ExternalOpenForTests = null;
                File.Move(Path.Combine(source, "notes2.txt"), Path.Combine(source, "renamed.pdf"));
                Next("PNG preview decoded, external-open binding validated without launching apps, file click leaves walker untouched");
            }
            if (stage == 2)
            {
                if (!WaitFor(() => sync.Entries.TryGetValue(textId, out var entry) && entry.name == "renamed.pdf" && entry.kind == "document")) return false;
                int targetPage = sync.Entries[textId].slot / sync.Layout.Capacity; sync.ShowPage(targetPage);
                Next("Watcher rename preserves file ID and changes asset kind");
            }
            if (stage == 3)
            {
                if (!WaitFor(() => sync.Visible.Any(b => b.FileId == textId && b.Entry.kind == "document"))) return false;
                File.AppendAllText(Path.Combine(source, "renamed.pdf"), " updated content");
                Next("Page switching exposes the same stable ID with a document asset");
            }
            if (stage == 4)
            {
                if (!WaitFor(() => sync.Entries[textId].size > 3)) return false;
                File.WriteAllText(Path.Combine(source, "new.txt"), "new file");
                Next("File modifications update metadata");
            }
            if (stage == 5)
            {
                if (!WaitFor(() => sync.Entries.Values.Any(e => e.name == "new.txt"))) return false;
                sync.ShowPage(0); Next("Live file creation adds an indexed entity");
            }
            if (stage == 6)
            {
                if (!WaitFor(() => sync.Page == 0 && sync.Visible.Any(b => b.FileId == folderId))) return false;
                Require(runner.TryStart(new ActionCommand { action_id = "files-observe-test", name = "observe", object_id = folderId, duration_seconds = 10 }, e => actionResult = e), "start actual observation");
                Next("Character can observe a real file entity");
            }
            if (stage == 7)
            {
                if (!WaitFor(() => runner.Phase == "observing")) return false;
                File.Delete(Path.Combine(source, "资料夹/nested.txt")); Directory.Delete(Path.Combine(source, "资料夹"));
                Next("Remove only the isolated fixture target during its active action");
            }
            if (stage == 8)
            {
                if (!WaitFor(() => !sync.Entries.ContainsKey(folderId) && actionResult != null)) return false;
                Require(actionResult.state == "failed" && !runner.IsBusy, "removed target reports failure");
                Require(UnityEngine.Object.FindFirstObjectByType<DesktopWorldState>().SessionId == session && runner.ManualControlRevision == manualRevision, "sync preserves scene session and manual-control revision");
                sync.Tidy(); Next("Deletion removes entity, active observation fails honestly, sync preserves session and user-control state");
            }
            if (stage == 9)
            {
                if (!WaitFor(() => sync.Entries.Values.OrderBy(e => e.slot).Select(e => e.slot).SequenceEqual(Enumerable.Range(0, sync.Entries.Count)))) return false;
                sync.enabled = false; sync.enabled = true; sync.RequestSync(true);
                Next("Tidy packs slots without resetting file identity");
            }
            if (stage == 10)
            {
                if (!WaitFor(() => sync.Visible.Any() && !sync.IsScanning)) return false;
                Require(runner.ManualControlRevision == manualRevision, "sync re-enable does not take manual control");
                var target = sync.Visible.FirstOrDefault(b => b.GetComponent<DesktopWorldObject>().GetApproaches(sync.walker).Any(p => p.reachable && p.path_length > .7f));
                Require(target, "a distant file is reachable"); textId = target.FileId; actionResult = null;
                Require(runner.TryStart(new ActionCommand { action_id = "files-approach-test", name = "approach_object", object_id = textId, side = "nearest" }, e => actionResult = e), "start actual approach");
                Next("Watchers and entities recover after disable/re-enable");
            }
            if (stage == 11)
            {
                if (!WaitFor(() => sync.walker.IsMoving && sync.walker.ActualSpeed > .01f)) return false;
                string targetPath = sync.Entries[textId].path;
                Require(string.Equals(Path.GetDirectoryName(targetPath), source, StringComparison.OrdinalIgnoreCase), "delete only isolated fixture");
                File.Delete(targetPath); Next("Remove isolated file while character is walking toward it");
            }
            if (stage == 12)
            {
                if (!WaitFor(() => !sync.Entries.ContainsKey(textId) && actionResult != null)) return false;
                Require(actionResult.state == "failed" && !sync.walker.IsMoving, "removed approach target stops actual navigation");
                File.WriteAllText(Report, JsonUtility.ToJson(new TestReport { passed = true, checks = passed.Concat(new[] { "Removed approach target stops navigation and reports failure" }).ToArray(), files = sync.Entries.Count, slots = sync.Layout.Capacity }, true));
                Debug.Log("Desktop files runtime checks passed."); return true;
            }
            return false;
        }
    }
}
