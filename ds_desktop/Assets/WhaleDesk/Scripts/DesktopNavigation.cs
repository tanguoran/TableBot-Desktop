using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WhaleDesk
{
    // Only the desk is walkable: the floor and the tops of props are never navigation surfaces.
    [DefaultExecutionOrder(-300)]
    public sealed class DesktopNavigation : MonoBehaviour
    {
        public Bounds desktopBounds;
        [Tooltip("Radius of the feet on the desk, in metres. Includes clearance for passing props.")]
        [Min(.01f)] public float footRadius = .06f;
        public float agentRadius => footRadius;
        [Min(.01f)] public float agentHeight = .42f;
        public NavMeshData navigationData;
        NavMeshDataInstance instance;
        [HideInInspector] public float bakedRadius;
        NavMeshData runtimeData;

        public float SurfaceHeight => desktopBounds.max.y;
        public NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        public bool ContainsXZ(Vector3 point) => point.x >= desktopBounds.min.x && point.x <= desktopBounds.max.x &&
            point.z >= desktopBounds.min.z && point.z <= desktopBounds.max.z;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            // Older scenes contain a mesh already eroded for a 21 cm radius.
            if (!navigationData || !Mathf.Approximately(bakedRadius, footRadius)) runtimeData = BuildData();
            instance = NavMesh.AddNavMeshData(runtimeData ? runtimeData : navigationData);
        }

        void OnDisable()
        {
            if (instance.valid) instance.Remove();
            if (runtimeData) Destroy(runtimeData);
            runtimeData = null;
        }

        public NavMeshData BuildData()
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = .01f;
            settings.agentSlope = 30;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .012f;
            settings.overrideTileSize = true;
            settings.tileSize = 256;
            settings.minRegionArea = .01f;
            var sources = new List<NavMeshBuildSource> {
                new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(desktopBounds.center, Quaternion.identity, Vector3.one),
                    size = desktopBounds.size, area = 0
                }
            };
            var volume = desktopBounds;
            volume.Expand(new Vector3(1, 2, 1));
            return NavMeshBuilder.BuildNavMeshData(settings, sources, volume, Vector3.zero, Quaternion.identity);
        }

        public bool TrySample(Vector3 point, float tolerance, out Vector3 position)
        {
            position = default;
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return false;
            if (point.x < desktopBounds.min.x || point.x > desktopBounds.max.x ||
                point.z < desktopBounds.min.z || point.z > desktopBounds.max.z) return false;
            point.y = SurfaceHeight;
            if (!NavMesh.SamplePosition(point, out var hit, tolerance, Filter)) return false;
            // Sampling is deliberately local, so clicks cannot jump across an object to its far side.
            position = hit.position;
            return true;
        }

        struct NearestCandidate { public Vector3 position; public double score; }

        // Search the actual, radius-eroded and obstacle-carved mesh, then require a complete
        // path from this character. SamplePosition alone can choose an unreachable island.
        public bool TryNearestReachable(Vector3 requested, Vector3 origin, NavMeshPath path, out Vector3 position)
        {
            position = default;
            if (!float.IsFinite(requested.x) || !float.IsFinite(requested.y) || !float.IsFinite(requested.z)) return false;
            requested.y = SurfaceHeight;
            var mesh = NavMesh.CalculateTriangulation();
            var candidates = new List<NearestCandidate>();
            for (int i = 0; i < mesh.indices.Length; i += 3)
            {
                var a = mesh.vertices[mesh.indices[i]];
                var b = mesh.vertices[mesh.indices[i + 1]];
                var c = mesh.vertices[mesh.indices[i + 2]];
                // Only this flat desktop, never floors or another NavMesh at a different height.
                if (!ContainsXZ(a) || !ContainsXZ(b) || !ContainsXZ(c) ||
                    Mathf.Abs(a.y - SurfaceHeight) > .06f || Mathf.Abs(b.y - SurfaceHeight) > .06f ||
                    Mathf.Abs(c.y - SurfaceHeight) > .06f) continue;
                var projected = ClosestOnTriangleXZ(requested, a, b, c);
                candidates.Add(new NearestCandidate { position = projected, score = DistanceScore(requested, projected) });
            }
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            foreach (var candidate in candidates)
            {
                if (!NavMesh.SamplePosition(candidate.position, out var hit, .025f, Filter) ||
                    !ContainsXZ(hit.position) || Mathf.Abs(hit.position.y - SurfaceHeight) > .06f) continue;
                if (!NavMesh.CalculatePath(origin, hit.position, Filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                position = hit.position;
                return true;
            }
            return false;
        }

        // Remove the constant requested.sqrMagnitude term to keep ordering stable for very distant targets.
        static double DistanceScore(Vector3 requested, Vector3 p) => (double)p.x * p.x + (double)p.z * p.z -
            2 * ((double)requested.x * p.x + (double)requested.z * p.z);
        static double CrossXZ(Vector3 a, Vector3 b, Vector3 p) =>
            ((double)b.x - a.x) * ((double)p.z - a.z) - ((double)b.z - a.z) * ((double)p.x - a.x);
        static Vector3 OnSegmentXZ(Vector3 requested, Vector3 a, Vector3 b)
        {
            double dx = (double)b.x - a.x, dz = (double)b.z - a.z, length = dx * dx + dz * dz;
            double t = length > 1e-12 ? (((double)requested.x - a.x) * dx + ((double)requested.z - a.z) * dz) / length : 0;
            return Vector3.Lerp(a, b, (float)System.Math.Clamp(t, 0, 1));
        }
        static Vector3 ClosestOnTriangleXZ(Vector3 requested, Vector3 a, Vector3 b, Vector3 c)
        {
            double ab = CrossXZ(a, b, requested), bc = CrossXZ(b, c, requested), ca = CrossXZ(c, a, requested);
            if (System.Math.Abs(CrossXZ(a, b, c)) > 1e-12 &&
                !((ab < 0 || bc < 0 || ca < 0) && (ab > 0 || bc > 0 || ca > 0)))
                return new Vector3(requested.x, a.y, requested.z);
            var best = OnSegmentXZ(requested, a, b);
            var next = OnSegmentXZ(requested, b, c);
            if (DistanceScore(requested, next) < DistanceScore(requested, best)) best = next;
            next = OnSegmentXZ(requested, c, a);
            return DistanceScore(requested, next) < DistanceScore(requested, best) ? next : best;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.2f, .8f, 1, .6f);
            Gizmos.DrawWireCube(desktopBounds.center, desktopBounds.size);
        }
    }
}
