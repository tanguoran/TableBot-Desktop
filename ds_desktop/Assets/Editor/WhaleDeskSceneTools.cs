using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UniVRM10;

// Local scene-authoring commands. Requests live in Temp and never run in a player.
[InitializeOnLoad]
public static class WhaleDeskSceneTools
{
    const string Request = "Temp/WhaleDesk.request";
    const string Report = "Temp/WhaleDesk.report.json";
    const string Pack = "Assets/HQ_ResidentialHouse/";
    static bool busy;

    static WhaleDeskSceneTools() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
        catch (IOException) { return; }
        busy = true;
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "build") Build();
            else if (command == "capture") Capture();
        }
        catch (Exception e) { File.WriteAllText(Report, JsonUtility.ToJson(new Failure { error = e.ToString() }, true)); Debug.LogException(e); }
        finally { busy = false; }
    }

    [Serializable] class Failure { public string error; }
    [Serializable] class Item { public string path; public Vector3 center; public Vector3 size; public string[] children; }
    [Serializable] class Inspection { public Item[] assets; public string[] sourceDesks; }

    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }

    static void Inspect()
    {
        var names = new[] { "Desk", "DiningTable", "CoffeeTable", "ShelfA", "ShelfB", "LampDesk", "LaptopOpen", "PlantA", "Clock", "Whiteboard/WhiteboardStuffed", "Books/BooksSetA", "Books/BookD", "Deskware/NotebookA", "Deskware/PencilCase_filled", "Deskware/Stickers" };
        var items = new List<Item>();
        foreach (var name in names)
        {
            var path = Pack + "Prefabs/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) continue;
            var go = UnityEngine.Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            var b = BoundsOf(go);
            items.Add(new Item { path = path, center = b.center, size = b.size, children = go.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray() });
            UnityEngine.Object.DestroyImmediate(go);
        }
        var source = EditorSceneManager.OpenScene(Pack + "DemoScene/DemoScene_BakedSetup_Day.unity", OpenSceneMode.Additive);
        var desks = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == "Desk" || t.name.StartsWith("Desk ("))
            .Select(t => string.Join("/", Ancestors(t)) + " @ " + t.position + " children=" + string.Join(",", t.Cast<Transform>().Select(x => x.name))).ToArray();
        EditorSceneManager.CloseScene(source, true);
        File.WriteAllText(Report, JsonUtility.ToJson(new Inspection { assets = items.ToArray(), sourceDesks = desks }, true));
    }

    static IEnumerable<string> Ancestors(Transform t)
    {
        var result = new List<string>();
        while (t) { result.Add(t.name); t = t.parent; }
        result.Reverse(); return result;
    }

    const string OutputScene = "Assets/Scenes/WhaleDesk.unity";
    const string Art = "Assets/WhaleDesk/Materials/";

    [Serializable] class BuildResult { public string scene; public string source; public Vector3 deskSize; public Vector3 avatarSize; public float desktopHeight; public string[] missingShaders; }

    [MenuItem("Whale Desk/Rebuild Study Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the scene.");
        Directory.CreateDirectory(Art);
        // Preserve unsaved user work in a separate scene before changing the open scene set.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.isDirty && open.path != OutputScene)
                EditorSceneManager.SaveScene(open, "Assets/Scenes/" + open.name + "_BeforeWhaleDesk_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity", true);
        }

        var source = EditorSceneManager.OpenScene(Pack + "DemoScene/DemoScene_BakedSetup_Day.unity", OpenSceneMode.Additive);
        var originalDesk = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .First(t => t.name == "Desk" && t.parent && t.parent.name == "Interior");
        var desk = UnityEngine.Object.Instantiate(originalDesk.gameObject);
        desk.name = "Writing Desk - HQ Demo Adaptation";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(desk, scene);
        EditorSceneManager.CloseScene(source, true);
        var others = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s != scene).ToArray();
        foreach (var old in others) EditorSceneManager.CloseScene(old, true);
        SceneManager.SetActiveScene(scene);

        desk.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        desk.transform.localScale = new Vector3(2.15f, 1f, 2.25f);
        Clean(desk);
        var deskBounds = BoundsOf(desk);
        float top = deskBounds.max.y;
        var furniture = new GameObject("01 - Study Furniture").transform;
        desk.transform.SetParent(furniture, true);
        var props = new GameObject("02 - Desktop Objects").transform;

        var wall = Material("Warm Plaster", new Color(.42f, .40f, .35f), .12f);
        var trim = Material("Dark Walnut Trim", new Color(.075f, .052f, .033f), .32f);
        var brass = Material("Brushed Brass", new Color(.49f, .34f, .14f), .45f, .68f);
        var leather = Material("Midnight Blue Desk Leather", new Color(.025f, .052f, .067f), .24f);
        var walnut = Material("Oiled Wood Desktop", new Color(.78f, .66f, .51f), .38f);
        walnut.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Pack + "Textures/WoodDeck/WoodDeck_d.tga"));
        walnut.SetTextureScale("_BaseMap", new Vector2(1.2f, .70f));
        walnut.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Pack + "Textures/WoodDeck/WoodDeck_n.tga"));
        walnut.SetFloat("_BumpScale", .22f); walnut.EnableKeyword("_NORMALMAP");
        foreach (var renderer in desk.GetComponentsInChildren<Renderer>())
            if (renderer.name == "DeskGlass") renderer.sharedMaterial = walnut;
        var room = new GameObject("03 - Quiet Backdrop").transform;
        var floor = Material("Room Floor", new Color(.22f, .19f, .15f), .18f);
        Box("Room Floor", new Vector3(0, -.035f, 0), new Vector3(8, .06f, 8), floor, room);
        Box("Plaster Back Wall", new Vector3(0, 1.75f, 1.05f), new Vector3(8, 4, .12f), wall, room);
        Box("Desk Back Rail", new Vector3(0, top + .045f, .88f), new Vector3(3.40f, .09f, .065f), trim, furniture);
        Box("Wall Picture Rail", new Vector3(0, 1.47f, .97f), new Vector3(8, .018f, .035f), trim, room);
        Box("Writing Mat", new Vector3(-.03f, top + .003f, -.16f), new Vector3(1.35f, .006f, .58f), leather, props);
        Box("Writing Mat Brass Edge", new Vector3(-.03f, top + .001f, -.16f), new Vector3(1.37f, .004f, .60f), brass, props);

        Place("LampDesk", "Reading Lamp", new Vector3(-1.24f, top, .48f), .90f, 20, props);
        Place("LaptopOpen", "Laptop - Application Portal", new Vector3(-.77f, top, .40f), .66f, 180, props);
        Place("Books/BooksSetA", "Reference Books", new Vector3(-1.13f, top, .13f), .40f, 180, props);
        Place("Deskware/PencilCase_filled", "Pen Holder", new Vector3(-1.40f, top, .12f), .78f, 0, props);
        Place("Deskware/NotebookA", "Open Task Notebook", new Vector3(-.43f, top + .009f, -.27f), .82f, -12, props);
        Place("Deskware/Pen", "Writing Pen", new Vector3(-.25f, top + .012f, -.28f), .85f, -20, props);
        Place("Books/BookD", "Current Reading", new Vector3(.82f, top, -.29f), .68f, -9, props);
        Place("Books/BookB", "Reading Stack", new Vector3(1.10f, top, .27f), .85f, 8, props);
        Place("Books/BookC", "Reading Stack Top", new Vector3(1.11f, top + .025f, .27f), .80f, -4, props);
        Place("Deskware/Stickers", "Reminder Notes", new Vector3(1.32f, top, -.05f), .80f, 0, props);
        Place("PlantA", "Small Desktop Plant", new Vector3(1.40f, top, .55f), .27f, 15, props);
        Place("Whiteboard/WhiteboardStuffed", "Task Board", new Vector3(.91f, 1.32f, .96f), .67f, 180, room, false);
        Place("Clock", "Wall Clock", new Vector3(1.56f, 1.55f, .94f), .63f, 180, room, false);
        var shelf = Place("ShelfA", "Reference Shelf", new Vector3(-.90f, 1.14f, .93f), .83f, 180, room, false);
        foreach (var renderer in shelf.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = walnut;
        Place("Books/BooksSetA", "Shelf Books", new Vector3(-.91f, 1.17f, .79f), .58f, 180, room);

        var avatarAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Avatar/蓝色小肥鱼.vrm");
        if (!avatarAsset) throw new InvalidOperationException("The whale girl VRM has not imported successfully.");
        var avatar = (GameObject)PrefabUtility.InstantiatePrefab(avatarAsset, scene);
        avatar.name = "大肥鱼 - Desktop Companion";
        avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 180, 0));
        avatar.transform.localScale = Vector3.one;
        var ab = BoundsOf(avatar);
        avatar.transform.localScale = Vector3.one * (.40f / ab.size.y);
        RelaxArms(avatar);
        ab = BoundsOf(avatar);
        avatar.transform.position += new Vector3(.42f - ab.center.x, top - ab.min.y + .002f, -.02f - ab.center.z);
        var animator = avatar.GetComponent<Animator>();
        if (animator) animator.applyRootMotion = false;

        var lights = new GameObject("04 - Study Lighting").transform;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.52f, .49f, .45f);
        RenderSettings.ambientEquatorColor = new Color(.41f, .38f, .35f);
        RenderSettings.ambientGroundColor = new Color(.28f, .25f, .22f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(Pack + "DemoScene/DemoScene_Showcase/ReflectionProbe-0.exr");
        RenderSettings.reflectionIntensity = .58f;
        RenderSettings.fog = false;
        var key = Light("Soft Warm Key", LightType.Directional, new Vector3(-2, 4, -2), new Vector3(48, -25, 0), new Color(1f, .91f, .80f), 1.45f, lights);
        key.shadows = LightShadows.Soft;
        key.shadowStrength = .30f;
        key.shadowBias = .025f;
        key.shadowNormalBias = .12f;
        Light("Cool Front Fill", LightType.Directional, new Vector3(2, 3, -2), new Vector3(30, 145, 0), new Color(.80f, .87f, 1f), .40f, lights);
        Light("Front Softbox", LightType.Directional, new Vector3(0, 3, -3), new Vector3(28, 0, 0), new Color(1f, .94f, .87f), .32f, lights);
        var lamp = Light("Desk Lamp Pool", LightType.Spot, new Vector3(-1.25f, top + .37f, .39f), new Vector3(65, 0, 0), new Color(1f, .73f, .43f), 1.5f, lights);
        lamp.range = 2.2f; lamp.spotAngle = 106f; lamp.innerSpotAngle = 46f;

        var camGO = new GameObject("Main Camera - Fixed Frontal View");
        camGO.tag = "MainCamera";
        var camera = camGO.AddComponent<Camera>();
        camGO.transform.position = new Vector3(0, 2.03f, -2.95f);
        camGO.transform.LookAt(new Vector3(0, .96f, .08f));
        camera.fieldOfView = 38;
        camera.nearClipPlane = .03f; camera.farClipPlane = 25;
        camera.allowHDR = true;
        camera.backgroundColor = new Color(.12f, .13f, .14f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        var additional = camera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing = true;
        additional.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var volumeGO = new GameObject("05 - Color and Exposure");
        var volume = volumeGO.AddComponent<Volume>(); volume.isGlobal = true;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/WhaleDesk/DeskVolume.asset");
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, "Assets/WhaleDesk/DeskVolume.asset"); }
        if (!profile.TryGet<Tonemapping>(out var tonemap)) { tonemap = profile.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(tonemap, profile); }
        tonemap.mode.Override(TonemappingMode.ACES);
        if (!profile.TryGet<ColorAdjustments>(out var colors)) { colors = profile.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(colors, profile); }
        colors.postExposure.Override(.12f); colors.contrast.Override(1f); colors.saturation.Override(-5f);
        volume.sharedProfile = profile;

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, OutputScene);
        WhaleDeskNavigationTools.Install();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(OutputScene, true) }
            .Concat(EditorBuildSettings.scenes.Where(s => s.path != OutputScene).Select(s => new EditorBuildSettingsScene(s.path, false))).ToArray();
        Selection.activeGameObject = avatar;
        SceneView.lastActiveSceneView?.AlignViewToObject(camera.transform);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var bad = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .Where(m => m && (!m.shader || !m.shader.isSupported)).Select(m => m.name).Distinct().ToArray();
        File.WriteAllText(Report, JsonUtility.ToJson(new BuildResult { scene = OutputScene, source = Pack + "DemoScene/DemoScene_BakedSetup_Day.unity", deskSize = BoundsOf(desk).size, avatarSize = BoundsOf(avatar).size, desktopHeight = top, missingShaders = bad }, true));
        Debug.Log("Whale Desk scene saved: " + OutputScene);
    }

    static void Clean(GameObject go)
    {
        if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var script in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (script) UnityEngine.Object.DestroyImmediate(script);
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
        foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            renderer.lightmapIndex = -1;
            if (renderer is MeshRenderer meshRenderer) meshRenderer.receiveGI = ReceiveGI.LightProbes;
        }
    }

    static GameObject Place(string name, string label, Vector3 position, float scale, float yaw, Transform parent, bool onSurface = true)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Prefabs/" + name + ".prefab");
        if (!prefab) throw new FileNotFoundException(name);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = label; go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale;
        Clean(go);
        if (onSurface) go.transform.position += Vector3.up * (position.y - BoundsOf(go).min.y);
        return go;
    }

    static Material Material(string name, Color color, float smoothness, float metallic = 0)
    {
        var path = Art + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(m); return m;
    }

    static GameObject Box(string name, Vector3 position, Vector3 size, Material material, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent); go.transform.position = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }

    static Light Light(string name, LightType type, Vector3 position, Vector3 rotation, Color color, float intensity, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = position; go.transform.eulerAngles = rotation;
        var light = go.AddComponent<Light>(); light.type = type; light.color = color; light.intensity = intensity; return light;
    }

    static void RelaxArms(GameObject avatar)
    {
        var humanoid = avatar.GetComponent<UniHumanoid.Humanoid>();
        if (!humanoid) return;
        foreach (var side in new[] { -1, 1 })
        {
            var upper = humanoid.GetBoneTransform(side < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var elbow = humanoid.GetBoneTransform(side < 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = humanoid.GetBoneTransform(side < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!upper || !elbow || !hand) continue;
            upper.rotation = Quaternion.FromToRotation(elbow.position - upper.position, new Vector3(-side * .25f, -1, -.12f)) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, new Vector3(-side * .12f, -1, -.18f)) * elbow.rotation;
        }
    }

    static void Capture()
    {
        var camera = Camera.main;
        if (!camera) throw new InvalidOperationException("No camera in the desk scene.");
        var aspect = camera.aspect; camera.aspect = 16f / 9f;
        var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var request = new RenderPipeline.StandardRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(camera, request);
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
        Directory.CreateDirectory("DeskPreviews");
        File.WriteAllBytes("DeskPreviews/WhaleDesk.png", texture.EncodeToPNG());
        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(texture);
        camera.aspect = aspect;
        File.WriteAllText(Report, "{\"preview\":\"DeskPreviews/WhaleDesk.png\"}");
    }
}
