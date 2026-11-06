using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WhaleDesk;

[InitializeOnLoad]
public static class WhaleDeskNavigationTools
{
    const string Request = "Temp/WhaleDeskNavigation.request";
    const string Report = "Temp/WhaleDeskNavigation.report.json";
    const string PendingTest = "WhaleDesk.NavigationTest";
    static RuntimeChecks checks;

    static WhaleDeskNavigationTools() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Request))
        {
            string command;
            try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
            catch (IOException) { return; }
            try
            {
                if (command == "install") Install();
                else if (command == "test")
                {
                    if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
                    SessionState.SetBool(PendingTest, true);
                    EditorApplication.isPlaying = true;
                }
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "status") Write(Status());
            }
            catch (Exception e) { Write(new Result { error = e.ToString() }); Debug.LogException(e); }
        }
        if (!EditorApplication.isPlaying || !SessionState.GetBool(PendingTest, false)) return;
        if (Time.time < 1.5f) return; // Let obstacle carving settle before querying paths.
        if (checks == null) checks = new RuntimeChecks();
        try { if (checks.Tick()) { SessionState.SetBool(PendingTest, false); checks = null; } }
        catch (Exception e)
        {
            SessionState.SetBool(PendingTest, false); checks = null;
            Write(new Result { error = e.ToString() }); Debug.LogException(e);
        }
    }

    [Serializable] class Result
    {
        public string scene, error, state;
        public int obstacles, pathCorners;
        public Vector3 position, destination;
        public bool ready, passed;
        public string[] checks;
    }

    static void Write(Result result) { File.WriteAllText(Report, JsonUtility.ToJson(result, true)); }
    static Result Status()
    {
        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        return new Result { state = walker ? walker.State.ToString() : "Missing", ready = walker && walker.IsReady,
            position = walker ? walker.transform.position : default,
            destination = walker ? walker.Destination : default,
            error = walker ? walker.LastError : "No walker", pathCorners = walker ? walker.GetPathCorners().Length : 0 };
    }

    [MenuItem("Whale Desk/Install Walking System")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode before editing.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/WhaleDesk.unity") throw new InvalidOperationException("Open WhaleDesk.unity first.");
        var desk = GameObject.Find("Writing Desk - HQ Demo Adaptation");
        if (!desk) throw new InvalidOperationException("Desk missing.");
        var deskBounds = RenderBounds(desk);
        var nav = UnityEngine.Object.FindFirstObjectByType<DesktopNavigation>();
        if (!nav) nav = new GameObject("06 - Desktop Navigation").AddComponent<DesktopNavigation>();
        nav.desktopBounds = new Bounds(new Vector3(deskBounds.center.x, deskBounds.max.y - .002f, deskBounds.center.z),
            new Vector3(deskBounds.size.x, .02f, deskBounds.size.z));
        nav.agentHeight = .42f;
        var data = nav.BuildData();
        if (!data) throw new InvalidOperationException("Could not build desktop navigation.");
        const string assetPath = "Assets/WhaleDesk/DesktopNavMesh.asset";
        var oldData = AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath);
        if (oldData) { EditorUtility.CopySerialized(data, oldData); UnityEngine.Object.DestroyImmediate(data); nav.navigationData = oldData; }
        else { AssetDatabase.CreateAsset(data, assetPath); nav.navigationData = data; }
        nav.bakedRadius = nav.footRadius;

        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        if (!walker)
        {
            var avatar = GameObject.Find("大肥鱼 - Desktop Companion");
            if (!avatar) throw new InvalidOperationException("Whale girl missing.");
            var bounds = RenderBounds(avatar);
            var root = new GameObject("大肥鱼 - Action Root");
            root.transform.position = new Vector3(bounds.center.x, nav.SurfaceHeight, bounds.center.z);
            root.transform.rotation = avatar.transform.rotation;
            avatar.transform.SetParent(root.transform, true);
            avatar.transform.position += Vector3.up * (nav.SurfaceHeight - bounds.min.y + .002f);
            var agent = root.AddComponent<NavMeshAgent>(); agent.enabled = false;
            walker = root.AddComponent<DesktopWalker>(); walker.visual = avatar.transform;
        }
        walker.navigation = nav;
        WhaleDeskAnimationTools.BindExistingController(walker);
        var movementAgent = walker.GetComponent<NavMeshAgent>();
        movementAgent.enabled = false; movementAgent.radius = nav.agentRadius; movementAgent.height = nav.agentHeight;
        movementAgent.speed = walker.speed; movementAgent.stoppingDistance = .018f;
        movementAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        var props = GameObject.Find("02 - Desktop Objects");
        foreach (Transform prop in props.transform)
        {
            // Flat mat and loose pen are traversable; books/notebook/laptop/stationery are obstacles.
            if (prop.name.StartsWith("Writing Mat") || prop.name == "Writing Pen") continue;
            var obstacle = prop.GetComponent<DesktopObstacle>();
            if (!obstacle) obstacle = prop.gameObject.AddComponent<DesktopObstacle>();
            obstacle.navigation = nav;
        }
        var input = nav.GetComponent<DesktopClickToMove>();
        if (!input) input = nav.gameObject.AddComponent<DesktopClickToMove>();
        input.walker = walker; input.viewCamera = Camera.main;
        WhaleDeskMcpTools.ConfigureScene();
        EditorUtility.SetDirty(nav); EditorUtility.SetDirty(walker); EditorUtility.SetDirty(movementAgent); EditorUtility.SetDirty(input);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = walker.gameObject;
        Write(new Result { scene = scene.path, obstacles = UnityEngine.Object.FindObjectsByType<DesktopObstacle>(FindObjectsSortMode.None).Length });
        Debug.Log("Desktop walking installed. Play, then left-click the desktop; Escape cancels.");
    }

    static Bounds RenderBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    // Runs the real agent over the real desk in play mode, checking its swept footprint each frame.
    sealed class RuntimeChecks
    {
        readonly DesktopWalker walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        readonly List<string> results = new List<string>();
        DesktopObstacle[] obstacles;
        int stage;
        float deadline;
        Vector3 target, before, initial;
        int maximumCorners;
        GameObject barrier;
        float barrierTime;
        float lastTime = -1;

        void Require(bool condition, string message) { if (!condition) throw new Exception("Navigation check: " + message); }

        public bool Tick()
        {
            if (Time.time == lastTime) return false;
            lastTime = Time.time;
            Require(walker && walker.IsReady, "agent must be ready");
            if (stage == 0)
            {
                obstacles = UnityEngine.Object.FindObjectsByType<DesktopObstacle>(FindObjectsSortMode.None);
                initial = walker.transform.position;
                Require(walker.TryMoveTo(new Vector3(8, 0, 0)) && walker.DestinationAdjusted, "off-desk target must resolve on desk");
                walker.CancelMove();
                Require(!walker.TryMoveTo(new Vector3(float.NaN, 0, 0)), "non-finite target must be rejected");
                var book = obstacles.First(o => o.name == "Current Reading");
                book.TryGetFootprint(out var bookBounds);
                Require(!walker.TryMoveTo(bookBounds.center), "book interior must be rejected");
                results.Add("Projected off-desk destination; rejected non-finite and occupied destinations");
                // The straight line crosses the right-hand book, requiring a genuine detour.
                target = new Vector3(1.33f, walker.navigation.SurfaceHeight, -.47f);
                Require(walker.TryMoveTo(target), "detour target should be reachable: " + walker.LastError);
                maximumCorners = walker.GetPathCorners().Length;
                Require(maximumCorners > 2, "path must bend around the book");
                deadline = Time.time + 16; stage = 1;
            }
            CheckPosition();
            maximumCorners = Mathf.Max(maximumCorners, walker.GetPathCorners().Length);
            if (stage == 1 && walker.State == WalkState.Arrived)
            {
                Require(Vector3.Distance(walker.transform.position, target) < .045f, "arrival tolerance");
                results.Add("Walked around Current Reading book and arrived within 4.5 cm");
                target = new Vector3(-.97f, walker.navigation.SurfaceHeight, -.49f);
                Require(walker.TryMoveTo(target), "leftward target must be reachable");
                before = walker.transform.position;
                deadline = Time.time + 18; stage = 2;
            }
            if (stage == 2 && Vector3.Distance(before, walker.transform.position) > .12f)
            {
                var destination = walker.Destination;
                Require(!walker.TryMoveTo(new Vector3(float.NaN, 0, 0)), "invalid request during walking");
                Require(walker.IsMoving && walker.Destination == destination, "invalid request must preserve active move");
                Require(walker.TryMoveTo(initial), "new destination replaces running move");
                target = initial; deadline = Time.time + 16; stage = 3;
                results.Add("Retargeted during walking; invalid request preserved active command");
            }
            if (stage == 3 && walker.State == WalkState.Arrived)
            {
                results.Add("Returned to initial position");
                // Full-depth wall splits the desktop, verifying rejection of partial paths.
                barrier = new GameObject("TEST ONLY - Impassable Barrier");
                barrier.transform.position = new Vector3(0, walker.navigation.SurfaceHeight + .25f, 0);
                var block = barrier.AddComponent<NavMeshObstacle>();
                block.shape = NavMeshObstacleShape.Box; block.size = new Vector3(.12f, .6f, 2.5f);
                block.carving = true; block.carveOnlyStationary = false;
                barrierTime = Time.time; deadline = Time.time + 6; stage = 4;
            }
            if (stage == 4 && Time.time > barrierTime + .7f)
            {
                Require(!walker.TryMoveTo(new Vector3(-.97f, walker.navigation.SurfaceHeight, -.49f)), "disconnected target must be rejected");
                UnityEngine.Object.Destroy(barrier); barrierTime = Time.time; stage = 5;
                results.Add("Rejected unreachable destination across a full-depth obstacle");
            }
            if (stage == 5 && Time.time > barrierTime + .7f)
            {
                Require(walker.TryMoveTo(new Vector3(-.97f, walker.navigation.SurfaceHeight, -.49f)), "removed obstacle should reopen route");
                walker.CancelMove();
                Require(walker.State == WalkState.Idle && walker.GetPathCorners().Length == 0, "cancel must clear movement");
                results.Add("Removing obstacle reopened route; cancellation cleared path");
                Write(new Result { passed = true, state = walker.State.ToString(), ready = walker.IsReady,
                    position = walker.transform.position, pathCorners = maximumCorners, checks = results.ToArray() });
                Debug.Log("Desktop navigation checks passed: " + results.Count);
                return true;
            }
            Require(walker.State != WalkState.Failed, "movement failed: " + walker.LastError);
            Require(Time.time < deadline, "movement timed out at stage " + stage);
            return false;
        }

        void CheckPosition()
        {
            var point = walker.transform.position;
            var bounds = walker.navigation.desktopBounds;
            float radius = walker.navigation.agentRadius - .025f; // voxel quantization tolerance
            Require(point.x >= bounds.min.x + radius && point.x <= bounds.max.x - radius &&
                point.z >= bounds.min.z + radius && point.z <= bounds.max.z - radius, "footprint must stay on desk");
            foreach (var obstacle in obstacles)
            {
                if (!obstacle.TryGetFootprint(out var b)) continue;
                float dx = Mathf.Max(b.min.x - point.x, 0, point.x - b.max.x);
                float dz = Mathf.Max(b.min.z - point.z, 0, point.z - b.max.z);
                Require(dx * dx + dz * dz >= radius * radius, "collided with " + obstacle.name);
            }
        }
    }
}
