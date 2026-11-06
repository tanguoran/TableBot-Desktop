using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WhaleDesk
{
    public sealed class DesktopFileLayout : MonoBehaviour
    {
        public DesktopNavigation navigation;
        [Range(2, 5)] public int columns = 4;
        public Vector2 rowPositions = new Vector2(-.48f, .40f);
        public List<Vector3> Positions { get; private set; } = new List<Vector3>();
        public int Capacity => Mathf.Max(1, Positions.Count);

        public void BuildSlots()
        {
            Positions.Clear();
            var desk = navigation.desktopBounds;
            float edge = .45f;
            foreach (float z in new[] { rowPositions.x, rowPositions.y })
                for (int col = 0; col < columns; col++)
                {
                    float x = Mathf.Lerp(desk.min.x + edge, desk.max.x - edge, col / (float)(columns - 1));
                    var point = new Vector3(x, navigation.SurfaceHeight, z);
                    var box = new Bounds(point + Vector3.up * .10f, new Vector3(.23f, .20f, .21f));
                    bool blocked = false;
                    foreach (var obstacle in FindObjectsByType<DesktopObstacle>(FindObjectsSortMode.None))
                    {
                        if (obstacle.GetComponentInParent<DesktopFileBinding>()) continue;
                        if (obstacle.TryGetFootprint(out var footprint) && box.Intersects(footprint)) { blocked = true; break; }
                    }
                    if (!blocked) Positions.Add(point);
                }
            if (Positions.Count == 0) Positions.Add(new Vector3(desk.center.x, navigation.SurfaceHeight, rowPositions.x));
        }

        public static IEnumerable<DesktopFileEntry> Ordered(IEnumerable<DesktopFileEntry> entries) => entries
            .OrderBy(e => DesktopFileCatalog.Order(e.kind)).ThenBy(e => e.name, DesktopFileNameComparer.Instance)
            .ThenBy(e => e.path, StringComparer.OrdinalIgnoreCase);

        public static void AssignSlots(IEnumerable<DesktopFileEntry> entries, bool tidy)
        {
            var ordered = Ordered(entries).ToArray();
            var used = new HashSet<int>();
            if (tidy) { for (int i = 0; i < ordered.Length; i++) ordered[i].slot = i; return; }
            foreach (var entry in ordered)
                if (entry.slot < 0 || !used.Add(entry.slot)) entry.slot = -1;
            foreach (var entry in ordered.Where(e => e.slot < 0))
            {
                int slot = 0; while (used.Contains(slot)) slot++;
                entry.slot = slot; used.Add(slot);
            }
        }
    }
}
