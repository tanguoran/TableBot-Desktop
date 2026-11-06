using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using WhaleDesk;

// Run in the actual desk scene, with current prop carving and the real character agent.
[InitializeOnLoad]
public static class WhaleDeskDestinationTests
{
    const string Request = "Temp/WhaleDeskDestination.request", Report = "Temp/WhaleDeskDestination.report.json";
    static Checks checks;
    static WhaleDeskDestinationTests() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (File.Exists(Request))
            {
                string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
                if (command == "test")
                {
                    if (!EditorApplication.isPlaying) throw new Exception("Enter Play mode first");
                    checks = new Checks();
                }
            }
            if (checks != null && checks.Tick()) checks = null;
        }
        catch (Exception error)
        {
            checks?.Cleanup(); checks = null;
            File.WriteAllText(Report, JsonUtility.ToJson(new Result { error = error.ToString() }, true));
            Debug.LogException(error);
        }
    }
    [Serializable] class Result { public bool passed; public string error; public string[] checks; public Vector3 restored_position; }
    sealed class Checks
    {
        readonly DesktopWalker walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        readonly List<string> results = new List<string>();
        Vector3 original, chosen;
        GameObject barrier;
        int stage;
        float deadline = Time.time + 30, since, lastTime = -1;
        static void Require(bool value, string message) { if (!value) throw new Exception("Destination check: " + message); }
        public void Cleanup() { if (barrier) UnityEngine.Object.Destroy(barrier); if (walker) walker.CancelMove(); }
        public bool Tick()
        {
            if (!EditorApplication.isPlaying) { Cleanup(); return true; }
            if (Time.time == lastTime) return false;
            lastTime = Time.time;
            Require(Time.time < deadline, "timed out at stage " + stage);
            if (stage == 0)
            {
                if (Time.time < 1.5f || !walker.IsReady) return false;
                original = walker.transform.position;
                foreach (var goal in new[] { new Vector3(8, 0, 0), new Vector3(-8, 0, 0),
                    new Vector3(0, 0, 8), new Vector3(0, 0, -8), new Vector3(100, 0, 100),
                    new Vector3(float.MaxValue, 0, -float.MaxValue) })
                {
                    Require(walker.TryResolveDestination(goal, out var point, out var adjusted) && adjusted, "outside target must resolve");
                    Require(walker.navigation.ContainsXZ(point) && walker.CanReach(point), "fallback must be on reachable desk");
                }
                results.Add("All sides, diagonal and extreme finite targets resolve on the reachable desktop");
                Require(!walker.TryResolveDestination(new Vector3(float.NaN, 0, 0), out _, out _), "invalid target must be rejected");
                int wallCount = 0;
                foreach (var item in DesktopWorldObject.Perceived.Where(o => o.objectId == "board_01" || o.objectId == "clock_01"))
                {
                    if (walker.navigation.ContainsXZ(item.WorldBounds().center)) continue;
                    var approaches = item.GetApproaches(walker);
                    Require(approaches.Any(p => p.reachable && p.destination_adjusted), "wall item must expose a reachable fallback");
                    wallCount++;
                }
                Require(wallCount > 0, "scene should contain an off-desk wall item");
                results.Add("Wall objects expose reachable approaches without editing their Can Approach flag");
                var goalRight = new Vector3(8, walker.navigation.SurfaceHeight, 0);
                Require(walker.TryMoveTo(goalRight, 90), "right fallback movement must start");
                chosen = walker.Destination;
                Require(walker.DestinationAdjusted && walker.RequestedDestination == goalRight, "original and actual destinations must be retained");
                Require(!walker.TryMoveTo(new Vector3(float.NaN, 0, 0)) && walker.Destination == chosen && walker.IsMoving,
                    "invalid request must preserve an active movement");
                CheckNearest(goalRight, chosen);
                stage = 1; deadline = Time.time + 20;
            }
            if (stage == 1 && walker.State == WalkState.Arrived)
            {
                Require(Vector3.Distance(walker.transform.position, chosen) < .045f, "must actually arrive at fallback");
                Require(Mathf.Abs(Mathf.DeltaAngle(walker.transform.eulerAngles.y, 90)) < 1, "arrival heading must be preserved");
                results.Add("Arrived at nearest fallback with requested heading, keeping the character footprint on desk");
                var bounds = walker.navigation.desktopBounds;
                barrier = new GameObject("TEST ONLY - Destination Split Barrier");
                barrier.transform.position = new Vector3(0, walker.navigation.SurfaceHeight + .3f, bounds.center.z);
                var obstacle = barrier.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = new Vector3(.12f, .8f, bounds.size.z + 2);
                obstacle.carving = true; obstacle.carveOnlyStationary = false;
                since = Time.time; stage = 2; deadline = Time.time + 10;
            }
            if (stage == 2 && Time.time > since + .8f)
            {
                var leftOutside = new Vector3(-8, walker.navigation.SurfaceHeight, 0);
                Require(walker.TryResolveDestination(leftOutside, out chosen, out var adjusted) && adjusted && chosen.x > 0,
                    "nearest disconnected island must be skipped");
                CheckNearest(leftOutside, chosen);
                Require(walker.TryMoveTo(leftOutside), "must move to fallback on the character's own island");
                results.Add("Full-depth carved barrier: selected nearest point in the connected region, not the nearer unreachable island");
                stage = 3; deadline = Time.time + 20;
            }
            if (stage == 3 && walker.State == WalkState.Arrived)
            { UnityEngine.Object.Destroy(barrier); since = Time.time; stage = 4; deadline = Time.time + 10; }
            if (stage == 4 && Time.time > since + .8f)
            {
                Require(walker.TryMoveTo(original, 180), "must restore original position after removing test barrier");
                stage = 5; deadline = Time.time + 20;
            }
            if (stage == 5 && walker.State == WalkState.Arrived)
            {
                results.Add("Removed test barrier and restored the character's original position");
                File.WriteAllText(Report, JsonUtility.ToJson(new Result { passed = true,
                    checks = results.ToArray(), restored_position = walker.transform.position }, true));
                return true;
            }
            if (stage > 0)
            {
                var p = walker.transform.position; var b = walker.navigation.desktopBounds; float radius = walker.navigation.agentRadius - .025f;
                Require(p.x >= b.min.x + radius && p.x <= b.max.x - radius && p.z >= b.min.z + radius && p.z <= b.max.z - radius,
                    "character footprint left desk");
                Require(walker.State != WalkState.Failed, "movement failed: " + walker.LastError);
            }
            return false;
        }
        void CheckNearest(Vector3 goal, Vector3 selected)
        {
            // Independent dense-grid oracle: the chosen exact projection must beat every reachable grid sample.
            float best = float.PositiveInfinity; var b = walker.navigation.desktopBounds;
            for (float x = b.min.x; x <= b.max.x; x += .08f)
            for (float z = b.min.z; z <= b.max.z; z += .08f)
            {
                var p = new Vector3(x, walker.navigation.SurfaceHeight, z);
                if (!walker.navigation.TrySample(p, .01f, out var sampled) || !walker.CanReach(sampled)) continue;
                best = Mathf.Min(best, Vector2.Distance(new Vector2(goal.x, goal.z), new Vector2(sampled.x, sampled.z)));
            }
            Require(Vector2.Distance(new Vector2(goal.x, goal.z), new Vector2(selected.x, selected.z)) <= best + .015f,
                "fallback must be nearest in the reachable region");
        }
    }
}
