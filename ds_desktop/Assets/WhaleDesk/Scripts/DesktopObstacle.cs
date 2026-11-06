using UnityEngine;
using UnityEngine.AI;

namespace WhaleDesk
{
    // A world-space proxy keeps carving correct even for rotated or scaled source prefabs.
    public sealed class DesktopObstacle : MonoBehaviour
    {
        public DesktopNavigation navigation;
        public float padding = .012f;
        public Transform physicalRoot;
        Renderer[] renderers;
        NavMeshObstacle[] obstacles;

        void OnEnable()
        {
            if (!Application.isPlaying || !navigation) return;
            renderers = (physicalRoot ? physicalRoot : transform).GetComponentsInChildren<Renderer>();
            obstacles = new NavMeshObstacle[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!(renderers[i] is MeshRenderer || renderers[i] is SkinnedMeshRenderer)) continue;
                var proxy = new GameObject(name + " - " + renderers[i].name + " footprint");
                proxy.transform.SetParent(navigation.transform, false);
                var obstacle = proxy.AddComponent<NavMeshObstacle>();
                obstacle.enabled = false;
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.carvingMoveThreshold = .005f;
                obstacles[i] = obstacle;
            }
            Update();
        }

        public bool TryGetFootprint(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var source = renderers ?? (physicalRoot ? physicalRoot : transform).GetComponentsInChildren<Renderer>();
            foreach (var renderer in source)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found || !navigation || bounds.min.y > navigation.SurfaceHeight + navigation.agentHeight ||
                bounds.max.y < navigation.SurfaceHeight - .02f) return false;
            bounds.Expand(new Vector3(padding * 2, 0, padding * 2));
            bounds.center = new Vector3(bounds.center.x, navigation.SurfaceHeight + .25f, bounds.center.z);
            bounds.size = new Vector3(bounds.size.x, .6f, bounds.size.z);
            return true;
        }

        void Update()
        {
            if (obstacles == null) return;
            for (int i = 0; i < obstacles.Length; i++)
            {
                var obstacle = obstacles[i];
                if (!obstacle) continue;
                var mesh = renderers[i];
                bool solid = mesh && mesh.enabled && mesh.gameObject.activeInHierarchy &&
                    mesh.bounds.min.y <= navigation.SurfaceHeight + navigation.agentHeight &&
                    mesh.bounds.max.y >= navigation.SurfaceHeight - .02f;
                if (!solid) { obstacle.enabled = false; continue; }
                // Project local bounds into the mesh's horizontal frame. A rotated book
                // no longer fills the empty corners of its world-axis-aligned bounding box.
                var yaw = Quaternion.Euler(0, mesh.transform.eulerAngles.y, 0);
                var frame = Matrix4x4.Rotate(Quaternion.Inverse(yaw)) * mesh.localToWorldMatrix;
                var bounds = mesh.localBounds;
                var e = bounds.extents;
                var size = new Vector3(
                    2 * (Mathf.Abs(frame.m00)*e.x + Mathf.Abs(frame.m01)*e.y + Mathf.Abs(frame.m02)*e.z),
                    .6f,
                    2 * (Mathf.Abs(frame.m20)*e.x + Mathf.Abs(frame.m21)*e.y + Mathf.Abs(frame.m22)*e.z));
                var center = mesh.localToWorldMatrix.MultiplyPoint3x4(bounds.center);
                center.y = navigation.SurfaceHeight + .25f;
                size += new Vector3(padding * 2, 0, padding * 2);
                if (obstacle.transform.position != center || obstacle.transform.rotation != yaw)
                    obstacle.transform.SetPositionAndRotation(center, yaw);
                if (obstacle.size != size) obstacle.size = size;
                obstacle.enabled = true;
            }
        }

        void OnDisable()
        {
            if (obstacles == null) return;
            foreach (var obstacle in obstacles) if (obstacle) Destroy(obstacle.gameObject);
            obstacles = null;
        }
    }
}
