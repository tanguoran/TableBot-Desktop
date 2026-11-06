using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace WhaleDesk
{
    [DisallowMultipleComponent]
    public sealed class DesktopWorldObject : MonoBehaviour
    {
        public string objectId;
        public string displayName;
        public string objectType = "prop";
        public string[] tags = Array.Empty<string>();
        public bool includeInState = true;
        public bool canApproach = true;
        public Transform physicalRoot;
        [Tooltip("Read-only semantic properties exposed to the model. Providers supply live state and real actions.")]
        public ObjectValue[] properties = Array.Empty<ObjectValue>();
        public ObjectValue[] states = Array.Empty<ObjectValue>();
        [Tooltip("Optional named approach transforms. Empty uses world front/back/left/right around the footprint.")]
        public Transform[] approachPoints = Array.Empty<Transform>();
        static readonly HashSet<DesktopWorldObject> Registry = new HashSet<DesktopWorldObject>();
        public static IEnumerable<DesktopWorldObject> Perceived => Registry.Where(o => o && o.isActiveAndEnabled && o.includeInState);

        void OnEnable()
        {
            Registry.Add(this);
            if (!Application.isPlaying) return;
            if (objectType == "lamp" && !GetComponent<DesktopLampInteraction>()) gameObject.AddComponent<DesktopLampInteraction>();
            if (GetComponent<DesktopFileBinding>() && !GetComponent<DesktopFileInteraction>()) gameObject.AddComponent<DesktopFileInteraction>();
        }
        DesktopObjectBehaviour[] Providers => GetComponents<DesktopObjectBehaviour>().Where(p => p.isActiveAndEnabled).ToArray();
        public ObjectInteraction[] Interactions() => Providers.SelectMany(p => p.Interactions()).ToArray();
        public bool Interact(string operation, out string error)
        {
            var matches = Providers.Where(p => p.Interactions().Any(a => a.operation == operation && a.available)).ToArray();
            if (matches.Length != 1) { error = "Operation unavailable or ambiguous"; return false; }
            try { return matches[0].Execute(operation, out error); }
            catch (Exception e) { error = e.Message; return false; }
        }
        void OnDisable() { Registry.Remove(this); }
        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(objectId)) objectId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }

        public Bounds WorldBounds()
        {
            bool found = false;
            var bounds = new Bounds(transform.position, Vector3.one * .03f);
            foreach (var renderer in (physicalRoot ? physicalRoot : transform).GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer ||
                    renderer.GetComponentInParent<DesktopWorldObject>() != this) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        public ApproachPoint[] GetApproaches(DesktopWalker walker)
        {
            if (!walker || !walker.navigation) return Array.Empty<ApproachPoint>();
            bool outside = !walker.navigation.ContainsXZ(WorldBounds().center);
            if (!canApproach && !outside) return Array.Empty<ApproachPoint>();
            var candidates = new List<ApproachPoint>();
            if (outside)
                candidates.Add(new ApproachPoint { side = "nearest", position = WorldBounds().center });
            else if (approachPoints != null && approachPoints.Any(p => p))
            {
                foreach (var point in approachPoints.Where(p => p))
                    candidates.Add(new ApproachPoint { side = point.name.ToLowerInvariant(), position = point.position });
            }
            else
            {
                var b = WorldBounds();
                float margin = walker.navigation.agentRadius + .05f;
                candidates.Add(new ApproachPoint { side = "front", position = new Vector3(b.center.x, 0, b.min.z - margin) });
                candidates.Add(new ApproachPoint { side = "back", position = new Vector3(b.center.x, 0, b.max.z + margin) });
                candidates.Add(new ApproachPoint { side = "left", position = new Vector3(b.min.x - margin, 0, b.center.z) });
                candidates.Add(new ApproachPoint { side = "right", position = new Vector3(b.max.x + margin, 0, b.center.z) });
            }
            foreach (var point in candidates)
            {
                point.id = objectId + "." + point.side;
                if (!outside) point.position = new Vector3(point.position.x, walker.navigation.SurfaceHeight, point.position.z);
                point.requested_position = point.position;
                if (!walker.TryResolveDestination(point.requested_position, out var sampled, out var adjusted)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(walker.transform.position, sampled, walker.navigation.Filter, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                point.position = sampled; point.reachable = true;
                point.destination_adjusted = adjusted;
                var corners = path.corners;
                for (int i = 1; i < corners.Length; i++) point.path_length += Vector3.Distance(corners[i - 1], corners[i]);
            }
            return candidates.ToArray();
        }

        public ObjectSnapshot Capture(DesktopWalker walker)
        {
            var bounds = WorldBounds();
            var obstacle = GetComponent<DesktopObstacle>();
            var providers = Providers;
            var interactions = Interactions();
            var capabilities = new List<string> { "observe", "look_at" };
            if (canApproach || (walker && walker.navigation && !walker.navigation.ContainsXZ(bounds.center))) capabilities.Add("approach_object");
            if (interactions.Any(a => a.available)) capabilities.Add("interact_object");
            return new ObjectSnapshot {
                object_id = objectId, display_name = displayName, object_type = objectType, tags = tags ?? Array.Empty<string>(),
                position = bounds.center, rotation_y = transform.eulerAngles.y,
                bounds = new BoundsSnapshot { min = bounds.min, max = bounds.max },
                is_obstacle = obstacle && obstacle.isActiveAndEnabled,
                capabilities = capabilities.ToArray(),
                properties = new[] { ObjectValue.Text("category", objectType) }.Concat(properties ?? Array.Empty<ObjectValue>()).Concat(providers.SelectMany(p => p.Properties())).ToArray(),
                state = new[] { ObjectValue.Bool("active", gameObject.activeInHierarchy), ObjectValue.Bool("can_interact", interactions.Any(a => a.available)) }
                    .Concat(states ?? Array.Empty<ObjectValue>()).Concat(providers.SelectMany(p => p.State())).ToArray(),
                interactions = interactions,
                approach_points = GetApproaches(walker), file = GetComponent<DesktopFileBinding>()?.Capture()
            };
        }
    }

    [Serializable] public sealed class BoundsSnapshot { public Vector3 min, max; }
    [Serializable] public sealed class ApproachPoint
    {
        public string id, side;
        public Vector3 position, requested_position;
        public bool reachable, destination_adjusted;
        public float path_length;
    }
    [Serializable] public sealed class ObjectSnapshot
    {
        public string object_id, display_name, object_type;
        public string[] tags, capabilities;
        public Vector3 position;
        public float rotation_y;
        public BoundsSnapshot bounds;
        public bool is_obstacle;
        public ApproachPoint[] approach_points;
        public DesktopFileMetadata file;
        public ObjectValue[] properties, state;
        public ObjectInteraction[] interactions;
    }
}
