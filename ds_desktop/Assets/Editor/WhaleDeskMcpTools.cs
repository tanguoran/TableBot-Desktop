using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WhaleDesk;

[InitializeOnLoad]
public static class WhaleDeskMcpTools
{
    const string Request = "Temp/WhaleDeskMcp.request";
    const string Report = "Temp/WhaleDeskMcp.report.json";
    static WhaleDeskMcpTools() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
        catch (IOException) { return; }
        try
        {
            if (command == "install") Install();
            else if (command == "play") EditorApplication.isPlaying = true;
            else if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "inspect")
            {
                var world = UnityEngine.Object.FindFirstObjectByType<DesktopWorldState>();
                var bridge = world.GetComponent<DesktopBridgeClient>();
                File.WriteAllText(Report, JsonUtility.ToJson(new Status {
                    connected = bridge.Connected, error = bridge.LastConnectionError, snapshot = world.Capture(),
                    pose = CapturePose(world.character.walker.animator)
                }, true));
            }
        }
        catch (Exception error) { File.WriteAllText(Report, JsonUtility.ToJson(new Status { error = error.ToString() }, true)); Debug.LogException(error); }
    }
    [Serializable] class Status { public bool connected; public string error; public WorldSnapshot snapshot; public PoseInfo pose; }
    [Serializable] class PoseInfo { public Vector3 hips, leftFoot, rightFoot, head; public string[] clips; }
    static PoseInfo CapturePose(Animator animator) => new PoseInfo {
        hips = animator.GetBoneTransform(HumanBodyBones.Hips).position,
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
        head = animator.GetBoneTransform(HumanBodyBones.Head).position,
        clips = animator.runtimeAnimatorController ? animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name).ToArray() : Array.Empty<string>()
    };

    [MenuItem("Whale Desk/Install Desktop MCP Integration")]
    public static void Install()
    {
        ConfigureScene();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DesktopWorldObject>(true)).ToArray();
        File.WriteAllText(Report, "{\"installed\":true,\"objects\":" + objects.Length + ",\"character_is_separate\":true}");
        Debug.Log("Desktop MCP integration installed: dedicated character state and " + objects.Length + " perceived objects.");
    }

    public static void ConfigureScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode before configuring MCP.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/WhaleDesk.unity") throw new InvalidOperationException("Open WhaleDesk.unity first.");
        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        if (!walker) throw new InvalidOperationException("Install the walking system first.");
        var character = walker.GetComponent<DesktopCharacterState>();
        if (!character) character = walker.gameObject.AddComponent<DesktopCharacterState>();
        character.walker = walker;
        var actions = walker.GetComponent<DesktopActionRunner>();
        if (!actions) actions = walker.gameObject.AddComponent<DesktopActionRunner>();
        actions.walker = walker;
        if (!walker.GetComponent<DesktopGazeController>()) walker.gameObject.AddComponent<DesktopGazeController>();
        WhaleDeskAnimationTools.BindActionClips(actions);
        EditorUtility.SetDirty(actions);
        var world = UnityEngine.Object.FindFirstObjectByType<DesktopWorldState>();
        if (!world) world = new GameObject("07 - Local Model MCP").AddComponent<DesktopWorldState>();
        world.character = character; world.navigation = walker.navigation;
        var bridge = world.GetComponent<DesktopBridgeClient>();
        if (!bridge) bridge = world.gameObject.AddComponent<DesktopBridgeClient>();
        bridge.world = world;

        var stack = GameObject.Find("Reading Stack");
        var stackTop = GameObject.Find("Reading Stack Top");
        if (stack && stackTop && !stackTop.GetComponent<DesktopWorldObject>() && stackTop.transform.parent != stack.transform)
            stackTop.transform.SetParent(stack.transform, true);
        if (stack && stackTop && stackTop.transform.parent == stack.transform)
        {
            var duplicateObstacle = stackTop.GetComponent<DesktopObstacle>();
            if (duplicateObstacle) UnityEngine.Object.DestroyImmediate(duplicateObstacle);
        }

        Mark("Reading Lamp", "lamp_01", "阅读台灯", "lamp", new[] { "work", "lighting" });
        Mark("Laptop - Application Portal", "laptop_01", "笔记本电脑", "computer", new[] { "work", "device" });
        Mark("Reference Books", "reference_books", "左侧参考书", "book", new[] { "reading" });
        Mark("Pen Holder", "pen_holder", "笔筒", "stationery", new[] { "work" });
        Mark("Open Task Notebook", "notebook_01", "任务笔记本", "notebook", new[] { "work", "writing" });
        Mark("Writing Pen", "pen_01", "书写笔", "stationery", new[] { "writing" });
        Mark("Current Reading", "book_01", "右侧正在读的书", "book", new[] { "reading" });
        Mark("Reading Stack", "book_stack", "右侧书堆", "book", new[] { "reading" });
        // The top book is one logical stack with its base, rather than a duplicate object.
        Mark("Reminder Notes", "notes_01", "便签纸", "stationery", new[] { "work" });
        Mark("Small Desktop Plant", "plant_01", "桌面盆栽", "plant", new[] { "decoration" });
        Mark("Task Board", "board_01", "墙面任务板", "board", new[] { "work" }, false);
        Mark("Wall Clock", "clock_01", "墙上时钟", "clock", new[] { "time" }, false);
        EditorUtility.SetDirty(character); EditorUtility.SetDirty(world); EditorUtility.SetDirty(bridge);
        WhaleDeskFilesTools.ConfigureScene();
    }

    static void Mark(string label, string id, string displayName, string type, string[] tags, bool approach = true)
    {
        var go = GameObject.Find(label);
        if (!go) return;
        var item = go.GetComponent<DesktopWorldObject>();
        if (item) return; // Inspector customizations survive reinstalling navigation.
        item = go.AddComponent<DesktopWorldObject>();
        item.objectId = id; item.displayName = displayName; item.objectType = type;
        item.tags = tags; item.canApproach = approach;
        EditorUtility.SetDirty(item);
    }
}
