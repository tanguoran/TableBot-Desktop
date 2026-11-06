using System;
using System.Linq;
using UnityEngine;

namespace WhaleDesk
{
    public sealed class DesktopWorldState : MonoBehaviour
    {
        public DesktopNavigation navigation;
        public DesktopCharacterState character;
        public DesktopFileSync files;
        public string SessionId { get; private set; }
        int version;
        void Awake() { SessionId = Guid.NewGuid().ToString("N"); }

        public WorldSnapshot Capture(string actionId = "", string actionName = "")
        {
            var objects = DesktopWorldObject.Perceived.Where(o => o.gameObject.scene == gameObject.scene &&
                !o.GetComponentInParent<DesktopCharacterState>()).OrderBy(o => o.objectId).ToArray();
            if (objects.Any(o => string.IsNullOrWhiteSpace(o.objectId)) || objects.GroupBy(o => o.objectId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Perceived objects must have nonempty, unique objectId values.");
            var bounds = navigation.desktopBounds;
            return new WorldSnapshot {
                scene_session = SessionId, scene_name = gameObject.scene.name,
                coordinate_system = "world_xz", unit = "meter", version = ++version,
                desktop = new DesktopSnapshot { bounds = new BoundsSnapshot { min = bounds.min, max = bounds.max }, surface_height = navigation.SurfaceHeight },
                character = character.Capture(actionId, actionName), objects = objects.Select(o => o.Capture(character.walker)).ToArray(),
                available_actions = new[] { "move_to", "approach_object", "cancel_move", "sit_down", "stand_up", "observe", "look_at", "jump", "set_expression", "interact_object" },
                desktop_files = files && files.isActiveAndEnabled && Application.isPlaying ? files.Capture() : null
            };
        }
    }

    [Serializable] public sealed class DesktopSnapshot { public BoundsSnapshot bounds; public float surface_height; }
    [Serializable] public sealed class WorldSnapshot
    {
        public string scene_session, scene_name, coordinate_system, unit;
        public int version;
        public DesktopSnapshot desktop;
        public CharacterSnapshot character;
        public ObjectSnapshot[] objects;
        public string[] available_actions;
        public DesktopFilesSnapshot desktop_files;
    }
}
